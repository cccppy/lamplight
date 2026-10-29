using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public bool TtsActive;
        public int TtsStart = -1;
        public int TtsEnd = -1;
        public DateTime TtsDeadline = DateTime.MinValue;
        int TtsGen;
        volatile bool TtsStop;
        Thread TtsThread;

        // 朗读浮条命中区
        RectangleF[] TtsRects = new RectangleF[0];
        public int TtsHover = -1;
        public int TtsPressed = -1;

        public void ToggleTts()
        {
            if (TtsActive) { StopTts(); return; }
            StartTts();
        }

        public void StartTts()
        {
            StartTts(-1);
        }

        public void StartTts(int explicitOffset)
        {
            if (!Tts.Available) { ShowToast("系统没有可用的语音引擎"); return; }
            if (Layout == null || ChapterBody.Length == 0) return;
            StopTts();
            List<int[]> spans = Tts.Split(ChapterBody);
            if (spans.Count == 0) return;

            int topOffset = explicitOffset >= 0
                ? Math.Max(0, Math.Min(ChapterBody.Length - 1, explicitOffset))
                : FirstVisibleOffset();
            int startIdx = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                if (spans[i][0] <= topOffset && topOffset < spans[i][0] + spans[i][1]) { startIdx = i; break; }
            }

            int minutes = Store.Lib.Settings.TtsAutoStopMinutes;
            if (TtsDeadline == DateTime.MinValue)
                TtsDeadline = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MinValue;

            TtsStop = false;
            TtsActive = true;
            int gen = ++TtsGen;
            try { File.WriteAllText(Path.Combine(Tts.TempDir(), "tts-log.txt"),
                "start len=" + ChapterBody.Length + " spans=" + spans.Count + "\r\n", Encoding.UTF8); } catch { }
            string body = ChapterBody;
            int idx = startIdx;

            TtsThread = new Thread(delegate()
            {
                string fail = null;
                int fastCount = 0;
                try
                {
                    SynthSlot next = Prefetch(spans, idx, body, gen);
                    while (!TtsStop && gen == TtsGen && idx < spans.Count)
                    {
                        SynthSlot cur = next;
                        int s = spans[idx][0];
                        if (s < 0 || s >= body.Length) break;
                        int len = Math.Min(spans[idx][1], body.Length - s);
                        if (len <= 0) break;

                        // 一边播当前句，一边在后台合成下一句，句间不留空档
                        int nextIdx = idx + 1;
                        next = (nextIdx < spans.Count) ? Prefetch(spans, nextIdx, body, gen) : null;

                        if (cur != null) cur.Done.WaitOne(30000);
                        if (TtsStop || gen != TtsGen) break;
                        // 语速/音量在播放前变过，就用新值重合成这一句，保证“下一句生效”
                        if (cur != null && cur.Ok && cur.Wav != null)
                        {
                            int nowRate = Store.Lib.Settings.TtsRate;
                            int nowVol = Store.Lib.Settings.TtsVolume;
                            string nowVoice = Store.Lib.Settings.TtsVoice;
                            if (cur.Rate != nowRate || cur.Vol != nowVol || cur.Voice != nowVoice)
                            {
                                byte[] w2; string e2;
                                if (Tts.SynthChain(body.Substring(s, len), nowRate, nowVol, nowVoice, out w2, out e2))
                                {
                                    cur.Wav = w2; cur.Rate = nowRate; cur.Vol = nowVol; cur.Voice = nowVoice;
                                }
                            }
                        }
                        if (cur == null || !cur.Ok || cur.Wav == null)
                        {
                            fail = "朗读失败：" + (cur == null ? "合成线程异常" : (cur.Err ?? "未知原因"));
                            LogBadClip(s, len, null, fail);
                            try { File.AppendAllText(Path.Combine(Tts.TempDir(), "tts-log.txt"),
                                "SYNTH-FAIL idx=" + idx + " " + fail + "\r\n", Encoding.UTF8); } catch { }
                            ShowToast("第 " + (idx + 1) + " 句合成失败，已跳过");
                            fastCount++;
                            if (fastCount >= 5) break;
                            idx++;
                            next = (idx + 1 < spans.Count) ? Prefetch(spans, idx + 1, body, gen) : null;
                            continue;
                        }

                        int curStart = s, curLen2 = len;
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                if (gen != TtsGen) return;
                                TtsStart = curStart;
                                TtsEnd = curStart + curLen2;
                                EnsureVisible(curStart);
                                Invalidate();
                            });
                        }
                        catch { }

                        try { File.AppendAllText(Path.Combine(Tts.TempDir(), "tts-log.txt"),
                            "idx=" + idx + " s=" + s + " len=" + len + " wav=" + (cur.Wav == null ? 0 : cur.Wav.Length)
                            + " text=" + body.Substring(s, Math.Min(24, len)).Replace("\r", " ").Replace("\n", " ") + "\r\n",
                            Encoding.UTF8); } catch { }
                        bool playedOk = Tts.PlayWav(cur.Wav);
                        try { File.AppendAllText(Path.Combine(Tts.TempDir(), "tts-log.txt"),
                            "  play=" + playedOk + " fast=" + Tts.LastPlayTooFast + " err=" + Tts.LastPlayError + "\r\n", Encoding.UTF8); } catch { }
                        if (TtsStop || gen != TtsGen) break;

                        if (!playedOk)
                        {
                            fastCount++;
                            LogBadClip(curStart, curLen2, cur.Wav, Tts.LastPlayError);
                            try { File.AppendAllText(Path.Combine(Tts.TempDir(), "tts-log.txt"),
                                "  SKIP play-fail: " + Tts.LastPlayError + "\r\n", Encoding.UTF8); } catch { }
                            if (fastCount >= 5)
                            {
                                fail = "连续多句都放不出声：" + Tts.LastPlayError;
                                break;
                            }
                            idx++;
                            continue;
                        }
                        fastCount = 0;
                        idx++;
                    }
                }
                catch (Exception ex) { fail = "朗读异常：" + ex.Message; }
                finally
                {
                    string msg = fail;
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            if (gen != TtsGen) return;
                            TtsActive = false;
                            TtsStart = TtsEnd = -1;
                            if (msg != null) ShowToast(msg);
                            Invalidate();
                        });
                    }
                    catch { }
                }
            });
            TtsThread.IsBackground = true;
            TtsThread.SetApartmentState(ApartmentState.STA);
            TtsThread.Start();
            ShowToast(minutes > 0 ? "朗读中 · " + minutes + " 分钟后自动停止" : "朗读中 · 再按一次停止");
        }

        void LogBadClip(int start, int len, byte[] wav, string playErr)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss") + "  start=" + start + " len=" + len +
                              " wav=" + (wav == null ? 0 : wav.Length) +
                              " text=" + (ChapterBody.Length > start ? ChapterBody.Substring(start, Math.Min(len, 40)).Replace("\r", " ").Replace("\n", " ") : "") +
                              " playErr=" + playErr + "\r\n";
                File.AppendAllText(Path.Combine(Tts.TempDir(), "tts-bad.txt"), line, Encoding.UTF8);
            }
            catch { }
        }

        class SynthSlot
        {
            public byte[] Wav;
            public string Err;
            public bool Ok;
            public int Rate;
            public int Vol;
            public string Voice;
            public System.Threading.ManualResetEvent Done = new System.Threading.ManualResetEvent(false);
        }

        /// <summary>后台合成一句，供播放线程提前取用。</summary>
        SynthSlot Prefetch(System.Collections.Generic.List<int[]> spans, int i, string body, int gen)
        {
            SynthSlot slot = new SynthSlot();
            int s = spans[i][0];
            if (s < 0 || s >= body.Length) { slot.Done.Set(); return slot; }
            int len = Math.Min(spans[i][1], body.Length - s);
            if (len <= 0) { slot.Done.Set(); return slot; }
            string sentence = body.Substring(s, len);
            int rate = Store.Lib.Settings.TtsRate;
            int vol = Store.Lib.Settings.TtsVolume;
            string voice = Store.Lib.Settings.TtsVoice;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    if (gen != TtsGen) return;
                    byte[] wav;
                    string err;
                    slot.Rate = rate;
                    slot.Vol = vol;
                    slot.Voice = voice;
                    slot.Ok = Tts.SynthChain(sentence, rate, vol, voice, out wav, out err);
                    if (!slot.Ok && (sentence.IndexOf('“') >= 0 || sentence.IndexOf('”') >= 0 ||
                                     sentence.IndexOf('"') >= 0 || sentence.IndexOf('「') >= 0))
                    {
                        // 某些语音引擎遇到引号会直接合成失败：去掉引号再试，内容不能丢
                        string alt = sentence.Replace('“', ' ').Replace('”', ' ')
                                             .Replace('"', ' ').Replace('「', ' ').Replace('」', ' ');
                        alt = alt.Trim();
                        if (alt.Length > 0 && Tts.SynthChain(alt, rate, vol, voice, out wav, out err))
                        {
                            slot.Ok = true;
                            slot.Err = null;
                        }
                    }
                    slot.Wav = wav;
                    slot.Err = err;
                }
                catch (Exception ex) { slot.Err = ex.Message; }
                finally { slot.Done.Set(); }
            });
            return slot;
        }

        /// <summary>当前视口顶部那一行在正文里的起始偏移。</summary>
        public int FirstVisibleOffset()
        {
            if (Layout == null || Layout.Lines.Count == 0) return 0;
            int li = TextDoc.LineAtY(Layout, Math.Max(0f, ScrollY));
            while (li + 1 < Layout.Lines.Count && Layout.Lines[li].Y + Layout.Lines[li].H <= ScrollY + 1f) li++;
            return Layout.Lines[li].Start;
        }

        public void StopTts()
        {
            TtsGen++;
            TtsStop = true;
            Tts.StopPlayback();
            TtsActive = false;
            TtsStart = TtsEnd = -1;
            TtsDeadline = DateTime.MinValue;
            Invalidate();
        }

        public void TtsTick()
        {
            if (!TtsActive) return;
            if (TtsDeadline != DateTime.MinValue && DateTime.Now >= TtsDeadline)
            {
                StopTts();
                ShowToast("定时结束，已停止朗读");
            }
        }

        void EnsureVisible(int relOffset)
        {
            if (Layout == null || Layout.Lines.Count == 0) return;
            int li = TextDoc.LineOfOffset(Layout, relOffset);
            float y = HeadingH + Layout.Lines[li].Y;
            float viewH = ContentBottom() - ContentTop();
            if (y < ScrollY + 40 || y > ScrollY + viewH - 90)
            {
                ScrollY = Math.Max(0, y - viewH * 0.3f);
                ClampScroll();
            }
        }

        // ------------------------------------------------------------ 朗读浮条
        public RectangleF[] TtsBarRects()
        {
            float bw = 470, bh = 44;
            float x = (W - bw) / 2, y = Hh - 44 - bh - 14;
            string[] labels = { "−", "语速 " + Store.Lib.Settings.TtsRate, "+",
                "定时 " + TtsRemaining(), "停止" };
            float[] widths = { 40, 96, 40, 108, 78 };
            float cx = x + 14;
            RectangleF[] rects = new RectangleF[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                rects[i] = new RectangleF(cx, y + 8, widths[i], 28);
                cx += widths[i] + 8;
            }
            return rects;
        }

        void DrawTtsBar(Painter p, float w, float h)
        {
            if (!TtsActive) { TtsRects = new RectangleF[0]; return; }
            float bw = 470, bh = 44;
            float x = (w - bw) / 2, y = h - 44 - bh - 14;
            RectangleF bar = new RectangleF(x, y, bw, bh);
            p.Shadow(bar, 12, 60, 0, 4);
            p.FillRound(Th.Panel, bar, 22);
            p.Stroke(Th.Line, bar, 22, 1);

            string[] labels = { "−", "语速 " + Store.Lib.Settings.TtsRate, "+",
                "定时 " + TtsRemaining(), "停止" };
            TtsRects = TtsBarRects();
            for (int i = 0; i < TtsRects.Length; i++)
            {
                bool last = i == TtsRects.Length - 1;
                bool hover = TtsHover == i;
                bool pressed = TtsPressed == i;
                RectangleF r = TtsRects[i];
                if (pressed) r.Y += 1; // 按下的下沉感

                Color bg;
                Color fg;
                if (last)
                {
                    bg = pressed ? Color.FromArgb(255, 120, 92, 66) : (hover ? Color.FromArgb(255, 178, 96, 62) : Th.Accent);
                    fg = Color.White;
                }
                else
                {
                    bg = pressed ? Th.Accent : (hover ? Th.AccentSoft : Th.Panel);
                    fg = pressed ? Color.White : (hover ? Th.Accent : Th.Ink);
                }
                p.FillRound(bg, r, 14);
                if (!last && !pressed) p.Stroke(Th.Line, r, 14, 1);
                if (pressed && !last) p.Stroke(Th.Accent, r, 14, 1);
                float ty = r.Y + r.Height / 2;
                p.TextMidCenter(labels[i], p.F(11f, i == 1 || last), fg, r.X + r.Width / 2, ty);
            }
        }

        public int TtsHitIndex(PointF m)
        {
            if (!TtsActive) return -1;
            RectangleF[] rects = TtsBarRects();
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(m)) return i;
            return -1;
        }

        /// <summary>朗读浮条点击；返回 true 表示已处理。</summary>
        public bool TtsBarClick(PointF m)
        {
            if (!TtsActive) return false;
            RectangleF[] rects = TtsBarRects();
            for (int i = 0; i < rects.Length; i++)
            {
                if (!rects[i].Contains(m)) continue;
                switch (i)
                {
                    case 0:
                        Store.Lib.Settings.TtsRate = Math.Max(-10, Store.Lib.Settings.TtsRate - 1);
                        Store.Save();
                        ShowToast("语速 " + Store.Lib.Settings.TtsRate + "（下一句生效）");
                        return true;
                    case 1:
                        return true;
                    case 2:
                        Store.Lib.Settings.TtsRate = Math.Min(10, Store.Lib.Settings.TtsRate + 1);
                        Store.Save();
                        ShowToast("语速 " + Store.Lib.Settings.TtsRate + "（下一句生效）");
                        return true;
                    case 3:
                        AskTtsMinutes();
                        return true;
                    case 4:
                        StopTts();
                        return true;
                }
                Store.Save();
                Invalidate();
                return true;
            }
            return false;
        }

        /// <summary>改语速/音色后从当前句重新开始，保证立刻听得出变化。</summary>
        public void RestartTts()
        {
            if (!TtsActive) return;
            int at = TtsStart;
            TtsStop = true;
            Tts.StopPlayback();
            TtsActive = false;
            TtsStart = TtsEnd = -1;
            TtsDeadline = TtsDeadline; // 保留剩余定时
            StartTts(at);
        }

        /// <summary>弹框输入定时分钟数（0 = 关闭）。</summary>
        public void AskTtsMinutes()
        {
            string v = PromptForm.Ask(this, "定时停止", "多少分钟后自动停止朗读？填 0 表示关闭。",
                Store.Lib.Settings.TtsAutoStopMinutes.ToString(), Th);
            if (v == null) return;
            int min;
            if (!int.TryParse(v.Trim(), out min)) { ShowToast("请输入数字"); return; }
            min = Math.Max(0, Math.Min(180, min));
            Store.Lib.Settings.TtsAutoStopMinutes = min;
            Store.Save();
            ApplyTtsMinutes();
            ShowToast(min == 0 ? "已关闭定时" : "定时 " + min + " 分钟");
        }

        void CycleTtsMinutes()
        {
            int[] steps = { 0, 5, 15, 30, 60 };
            int v = Store.Lib.Settings.TtsAutoStopMinutes;
            int i = Array.IndexOf(steps, v);
            Store.Lib.Settings.TtsAutoStopMinutes = steps[(i + 1) % steps.Length];
            ApplyTtsMinutes();
        }

        /// <summary>定时改动后立刻生效：正在朗读就按新的分钟数从现在重新计时。</summary>
        public void ApplyTtsMinutes()
        {
            int minutes = Store.Lib.Settings.TtsAutoStopMinutes;
            TtsDeadline = (TtsActive && minutes > 0) ? DateTime.Now.AddMinutes(minutes) : DateTime.MinValue;
            Invalidate();
        }

        public string TtsRemaining()
        {
            if (!TtsActive || TtsDeadline == DateTime.MinValue) return "关";
            TimeSpan left = TtsDeadline - DateTime.Now;
            if (left.TotalSeconds <= 0) return "0:00";
            return ((int)left.TotalMinutes) + ":" + left.Seconds.ToString("00");
        }
    }
}
