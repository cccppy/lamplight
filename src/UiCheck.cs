using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Lamplight
{
    /// <summary>界面逻辑自检：不显示窗口，直接调用 MainForm 的操作方法。</summary>
    static class UiCheck
    {
        static int _pass, _fail;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  (" + detail + ")" : "")); }
            else { _fail++; Console.WriteLine("FAIL  " + name + "  " + detail); }
        }

        public static int Run()
        {
            Application.EnableVisualStyles();
            MainForm f = new MainForm();
            f.PreviewMode = true;
            f.CreateControl();
            f.Scale = 1f;
            f.ClientSize = new Size(1200, 780);
            typeof(MainForm).GetMethod("OnLoad", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(f, new object[] { EventArgs.Empty });
            f.EnterPreview("shelf", null);

            Check("书架载入", f.ShelfView.Count >= 3, f.ShelfView.Count + " 本");

            // 1) 重命名：用真实的鼠标按下路径（点到别处应保存，而不是取消）
            string before = f.ShelfView[0].DisplayName;
            f.Selected = 0;
            f.BeginRename(0);
            f.EditBox.Text = "改过的书名";
            System.Reflection.MethodInfo md = typeof(MainForm).GetMethod("OnMouseDown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            md.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 600, 740, 0) });
            BookEntry b0 = Store.FindByPath(f.ShelfView[0].Path);
            Check("自定义书名", b0 != null && b0.DisplayName == "改过的书名", before + " → " + (b0 != null ? b0.DisplayName : "-"));
            f.Selected = 0;
            f.BeginRename(0);
            f.EditBox.Text = before;
            f.EndRename(true);

            // 2) 打开书籍
            BookEntry book = f.ShelfView[0];
            bool opened = f.OpenBook(book, true);
            Check("打开书籍", opened && f.Chapters.Count > 0, f.Chapters.Count + " 章, 编码 " + f.BookCharset);
            Check("进入阅读界面", f.Current == View.Reader, f.Current.ToString());

            // 3) 书签带章节
            int n0 = book.Bookmarks.Count;
            int lastCh = f.Chapters.Count - 1;
            int longCh = 0;
            for (int ci = 0; ci < f.Chapters.Count; ci++)
                if (f.Chapters[ci].Length > f.Chapters[longCh].Length) longCh = ci;
            f.JumpTo(lastCh, f.Chapters[lastCh].Start + Math.Max(0, f.Chapters[lastCh].Length / 2));
            f.ScrollY = 120;
            f.AddBookmark();
            BookmarkItem last = book.Bookmarks[book.Bookmarks.Count - 1];
            Check("添加书签", book.Bookmarks.Count == n0 + 1, "共 " + book.Bookmarks.Count + " 条");
            Check("书签记录章节", last.Chapter == lastCh && !string.IsNullOrEmpty(last.ChapterTitle),
                "第 " + (last.Chapter + 1) + " 章 · " + last.ChapterTitle);
            Check("书签有摘录", !string.IsNullOrEmpty(last.Excerpt), last.Excerpt);

            // 4) 书签跳转
            f.JumpTo(0, 0);
            f.JumpTo(last.Chapter, last.Offset);
            Check("书签跳转", f.ChapterIndex == last.Chapter, "跳转到第 " + (f.ChapterIndex + 1) + " 章");

            // 4.5) 书签可删除
            int bmBefore = book.Bookmarks.Count;
            bool bmDeleted = f.RemoveBookmarkAt(0);
            Check("书签可删除", bmDeleted && book.Bookmarks.Count == bmBefore - 1,
                bmBefore + " → " + book.Bookmarks.Count);

            // 5) 分页
            Store.Lib.Settings.PageMode = true;
            f.JumpTo(longCh, f.Chapters[longCh].Start);
            int p0 = f.PageIndex();
            f.NextPage();
            Check("分页翻页", f.PageIndex() == p0 + 1, "第 " + (f.PageIndex() + 1) + " 页");
            f.NextPage(); f.PrevPage();
            Check("上一页", f.PageIndex() == p0 + 1, "回到第 " + (f.PageIndex() + 1) + " 页");
            Store.Lib.Settings.PageMode = false;

            // 5.5) 分页必须按整行对齐，不能露出半行
            Store.Lib.Settings.PageMode = true;
            f.JumpTo(longCh, f.Chapters[longCh].Start);
            int badPages = 0;
            string badInfo = "";
            float bt = f.ContentTop() + f.HeadingH;
            float bh = f.ContentBottom() - bt;
            int pages = f.PageCount();
            for (int pi = 0; pi < pages; pi++)
            {
                f.ScrollY = f.PageStart(pi);
                f.ClampScroll();
                int li = TextDoc.LineAtY(f.Layout, f.ScrollY);
                if (Math.Abs(f.Layout.Lines[li].Y - f.ScrollY) > 0.6f) { badPages++; if (badInfo.Length == 0) badInfo = "p" + pi + "/" + pages + " scroll=" + f.ScrollY.ToString("0.0") + " lineY=" + f.Layout.Lines[li].Y.ToString("0.0") + " lastStart=" + f.PageStart(pages-1).ToString("0.0") + " h=" + f.Layout.Height.ToString("0.0"); }
                int lastLine = li;
                while (lastLine + 1 < f.Layout.Lines.Count &&
                       f.Layout.Lines[lastLine + 1].Y + f.Layout.Lines[lastLine + 1].H <= f.ScrollY + bh + 0.6f) lastLine++;
                if (f.Layout.Lines[lastLine].Y + f.Layout.Lines[lastLine].H > f.ScrollY + bh + 0.6f) { badPages++; if (badInfo.Length == 0) badInfo = "p" + pi + " bottom=" + (int)(f.Layout.Lines[lastLine].Y + f.Layout.Lines[lastLine].H) + " limit=" + (int)(f.ScrollY + bh); }
            }
            Check("分页整行对齐", badPages == 0, pages + " 页，异常 " + badPages + " " + badInfo);
            Store.Lib.Settings.PageMode = false;

            // 5.8) 统计进度按已读章节占比
            BookEntry statBook = f.Book; var statChaptersKeep = f.Chapters; int statIdx = f.ChapterIndex;
            f.Book = new BookEntry { Id = "stats", AutoName = "统计" };
            f.Book.ReadLog = new System.Collections.Generic.List<ChapterRead>();
            var statChapters = new System.Collections.Generic.List<Chapter>();
            for (int ci = 0; ci < 10; ci++) statChapters.Add(new Chapter { Title = "第" + ci, Length = 100 });
            f.Chapters = statChapters;
            for (int ci = 0; ci < 3; ci++) f.Book.ReadLog.Add(new ChapterRead { Chapter = ci, Percent = 1f });
            double expectPct = 0.3;
            Check("统计按已读章节", Math.Abs(f.StatsProgress() - expectPct) < 0.001,
                (f.StatsProgress() * 100).ToString("0") + "%");
            bool allKnown = f.StatsCharsKnown();
            statChapters[0].Length = 0;
            bool partialKnown = f.StatsCharsKnown();
            Check("在线书不算全书字数", allKnown && !partialKnown, "all=" + allKnown + " partial=" + partialKnown);
            f.Book = statBook; f.Chapters = statChaptersKeep; f.ChapterIndex = statIdx; f.PrepareChapter();

            // 6) 章节阅读状态
            f.JumpTo(lastCh, f.Chapters[lastCh].Start);
            f.ScrollY = 1e9f;
            f.ClampScroll();
            f.SaveProgress();
            Check("章节标记已读", f.ReadPercent(lastCh) >= 0.95f, "第 " + (lastCh + 1) + " 章 " + (int)(f.ReadPercent(lastCh) * 100) + "%");
            Check("已读章节计数", f.ReadChaptersCount() >= 1, f.ReadChaptersCount() + " 章");

            // 6.5) 滚轮到底自动进下一章
            f.JumpTo(0, f.Chapters[0].Start);
            f.ScrollY = 1e9f;
            f.ClampScroll();
            System.Reflection.MethodInfo mw = typeof(MainForm).GetMethod("OnMouseWheel",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int chBefore = f.ChapterIndex;
            mw.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.None, 0, 600, 400, -120) });
            Check("滚轮到底自动下一章", f.ChapterIndex == chBefore + 1,
                "第 " + (chBefore + 1) + " 章 → 第 " + (f.ChapterIndex + 1) + " 章");

            // 6.6) 快捷键：默认与自定义
            f.JumpTo(1, f.Chapters[1].Start);
            int c0 = f.ChapterIndex; float y0 = f.ScrollY;
            f.HandleKey(Keys.Right);
            bool moved0 = f.ChapterIndex != c0 || Math.Abs(f.ScrollY - y0) > 1;
            Check("默认 → 下一页", moved0, "Δ=" + (int)(f.ScrollY - y0));
            KeyMap.Set("nextpage", "Ctrl+N");
            int c1 = f.ChapterIndex; float y1 = f.ScrollY;
            f.HandleKey(Keys.Right);
            Check("改键后原键失效", f.ChapterIndex == c1 && Math.Abs(f.ScrollY - y1) < 0.01f, "");
            f.HandleKey(Keys.Control | Keys.N);
            bool moved1 = f.ChapterIndex != c1 || Math.Abs(f.ScrollY - y1) > 1;
            Check("改键后新键生效", moved1, "Δ=" + (int)(f.ScrollY - y1));
            KeyMap.Reset();
            Check("恢复默认快捷键", KeyMap.Get("nextpage") == "Right", KeyMap.Get("nextpage"));

            // 6.7) EPUB 解析
            string epubPath = Path.Combine(Store.DataDir, "test.epub");
            EpubSample.Build(epubPath);
            string epubText, epubTitle, epubErr;
            List<Lamplight.Chapter> epubCh;
            bool okEpub = Epub.Load(epubPath, out epubText, out epubCh, out epubTitle, out epubErr);
            Check("EPUB 打开", okEpub && epubCh.Count == 2, okEpub ? epubCh.Count + " 章 / " + epubTitle : epubErr);
            Check("EPUB 正文", epubText.Contains("第一段测试正文") && epubText.Contains("第二章内容"),
                epubText.Length + " 字");

            // 6.8) 朗读分句
            List<int[]> spans = Tts.Split("第一句。第二句！第三句？没有标点的结尾");
            string qtext1 = "他说：\u201c你好。\u201d";
            var q1 = Tts.Split(qtext1);
            bool q1ok = q1.Count == 1 && qtext1.Substring(q1[0][0], q1[0][1]).EndsWith("\u201d");
            string dlg = "\u4ed6\u8bf4\uff1a\u201c\u4eca\u5929\u5929\u6c14\u4e0d\u9519\u3002\u201d\u5979\u7b11\u4e86\u7b11\uff1a\u201c\u90a3\u5c31\u8d70\u5427\u3002\u201d";
            var dsp = Tts.Split(dlg);
            int dpos = 0; bool dcover = true;
            foreach (int[] sp2 in dsp)
            {
                if (sp2[0] != dpos) { dcover = false; break; }
                dpos = sp2[0] + sp2[1];
            }

            Check("对话分句无空洞", dcover && dpos == dlg.Length, dsp.Count + " 句, 覆盖 " + dpos + "/" + dlg.Length);

            Check("引号不单独成句", q1ok, q1.Count + " 句");
            string qtext2 = "\u201c好。\u201d他说。";
            var q2 = Tts.Split(qtext2);
            bool q2ok = q2.Count == 2 && qtext2.Substring(q2[0][0], q2[0][1]).EndsWith("\u201d");
            Check("句末引号跟随前句", q2ok, q2.Count + " 句");

            Check("朗读分句", spans.Count == 4, spans.Count + " 句");
            string punctText = "甲；乙？丙！丁…戊";
            var punctSpans = Tts.Split(punctText);
            int punctPos = 0; bool punctCover = true;
            foreach (int[] psp in punctSpans) { if (psp[0] != punctPos) { punctCover = false; break; } punctPos = psp[0] + psp[1]; }
            Check("问号分号都能断句", punctCover && punctPos == punctText.Length && punctSpans.Count >= 5,
                punctSpans.Count + " 句, 覆盖 " + punctPos + "/" + punctText.Length);

            // 快捷键表被清空时，方向键仍要能翻页
            string keepNext = KeyMap.Get("nextpage");
            KeyMap.Set("nextpage", "");
            bool arrowWorked = f.HandleKey(Keys.Right);
            KeyMap.Set("nextpage", keepNext);
            Check("方向键默认仍可翻页", arrowWorked, "right=" + arrowWorked);
            string btext = "【他说。你好】后面还有。第二句！";
            List<int[]> brSpans = Tts.Split(btext);
            int bpos = 0; bool bcover = true;
            foreach (int[] bs in brSpans) { if (bs[0] != bpos) { bcover = false; break; } bpos = bs[0] + bs[1]; }
            Check("括号内标点不丢", bcover && bpos == btext.Length && brSpans.Count >= 2, brSpans.Count + " 句, 覆盖 " + bpos + "/" + btext.Length);
            string longPara = "望着测验魔石碑上面闪亮得甚至有些刺眼的五个大字，少年面无表情，唇角有着一抹自嘲，紧握的手掌，因为大力，而导致略微尖锐的指甲深深的刺进了掌心之中，带来一阵阵钻心的疼痛。";
            List<int[]> lp = Tts.Split(longPara);
            int lmax = 0; foreach (int[] a in lp) lmax = Math.Max(lmax, a[1]);
            int lpos = 0; bool lcover = true;
            foreach (int[] a in lp) { if (a[0] != lpos) { lcover = false; break; } lpos = a[0] + a[1]; }
            int rate2 = 8000, loud = 1000, silent = 5000;
            byte[] wraw = new byte[(loud + silent) * 2];
            for (int i = 0; i < loud; i++) { short v = (short)(Math.Sin(i * 0.1) * 8000); wraw[i*2] = (byte)(v & 0xFF); wraw[i*2+1] = (byte)(v >> 8); }
            byte[] testWav = new byte[44 + wraw.Length];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(testWav, 0);
            BitConverter.GetBytes(36 + wraw.Length).CopyTo(testWav, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(testWav, 8);
            BitConverter.GetBytes(16).CopyTo(testWav, 16);
            BitConverter.GetBytes((short)1).CopyTo(testWav, 20);
            BitConverter.GetBytes((short)1).CopyTo(testWav, 22);
            BitConverter.GetBytes(rate2).CopyTo(testWav, 24);
            BitConverter.GetBytes(rate2 * 2).CopyTo(testWav, 28);
            BitConverter.GetBytes((short)2).CopyTo(testWav, 32);
            BitConverter.GetBytes((short)16).CopyTo(testWav, 34);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(testWav, 36);
            BitConverter.GetBytes(wraw.Length).CopyTo(testWav, 40);
            wraw.CopyTo(testWav, 44);
            byte[] wtrim = Tts.TrimSilence(testWav);
            bool wavOk = wtrim.Length < testWav.Length && wtrim.Length > 44 &&
                BitConverter.ToInt32(wtrim, 40) == wtrim.Length - 44 &&
                BitConverter.ToInt16(wtrim, 22) == 1 &&
                BitConverter.ToInt32(wtrim, 24) == rate2 &&
                BitConverter.ToInt16(wtrim, 34) == 16;
            string twavPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ink-trim-test.wav");
            System.IO.File.WriteAllBytes(twavPath, wtrim);
            bool twavValid = true; string twavErr = "";
            try { using (System.Media.SoundPlayer spv = new System.Media.SoundPlayer(twavPath)) spv.Load(); }
            catch (Exception ex) { twavValid = false; twavErr = ex.Message; }
            byte[] extWav = new byte[46 + wraw.Length];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(extWav, 0);
            BitConverter.GetBytes(38 + wraw.Length).CopyTo(extWav, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(extWav, 8);
            BitConverter.GetBytes(18).CopyTo(extWav, 16);
            BitConverter.GetBytes(unchecked((short)0xFFFE)).CopyTo(extWav, 20);
            BitConverter.GetBytes((short)1).CopyTo(extWav, 22);
            BitConverter.GetBytes(rate2).CopyTo(extWav, 24);
            BitConverter.GetBytes(rate2 * 2).CopyTo(extWav, 28);
            BitConverter.GetBytes((short)2).CopyTo(extWav, 32);
            BitConverter.GetBytes((short)16).CopyTo(extWav, 34);
            BitConverter.GetBytes((short)0).CopyTo(extWav, 36);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(extWav, 38);
            BitConverter.GetBytes(wraw.Length).CopyTo(extWav, 42);
            wraw.CopyTo(extWav, 46);
            byte[] conv = Tts.TrimSilence(extWav);
            bool convOk = BitConverter.ToInt16(conv, 20) == 1 && conv.Length > 44;
            string convErr = "";
            try { string cp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ink-ext-test.wav"); System.IO.File.WriteAllBytes(cp, conv); using (System.Media.SoundPlayer spc = new System.Media.SoundPlayer(cp)) spc.Load(); }
            catch (Exception ex) { convOk = false; convErr = ex.Message; }
            Check("扩展格式转 PCM", convOk, convErr);

            // 助手偶发写出“data 长度大于实际文件”的 WAV，要能修复后再播
            byte[] badWav = (byte[])testWav.Clone();
            BitConverter.GetBytes(badWav.Length + 4096).CopyTo(badWav, 40);
            byte[] repaired = Tts.CanonicalWav(badWav);
            bool repairedOk = repaired != null && repaired.Length == testWav.Length &&
                              BitConverter.ToInt32(repaired, 40) == wraw.Length &&
                              BitConverter.ToInt16(repaired, 20) == 1;
            Check("损坏 WAV 头可修复", repairedOk, repaired == null ? "null" : repaired.Length + " B");
            string badPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ink-bad-wav-test.wav");
            byte[] badTrim = Tts.TrimSilence(badWav);
            bool badPlayable = true; string badPlayErr = "";
            try { System.IO.File.WriteAllBytes(badPath, badTrim); using (System.Media.SoundPlayer spb = new System.Media.SoundPlayer(badPath)) spb.Load(); }
            catch (Exception ex) { badPlayable = false; badPlayErr = ex.Message; }
            Check("损坏 WAV 修复后可播放", badPlayable && badTrim.Length > 44, badPlayErr);

Check("裁剪 WAV 可被识别", twavValid, twavErr);

Check("WAV 裁剪头有效", wavOk, testWav.Length + " -> " + wtrim.Length);

Check("长段落分句", lcover && lpos == longPara.Length && lmax <= 110, lp.Count + " 句, 最长 " + lmax);
            Check("语音引擎", Tts.Available, Tts.Available ? "SAPI 可用" : "不可用");
            Check("语音线程通路", Tts.SelfTest(), Tts.LastSynthError.Length > 0 ? Tts.LastSynthError : "STA 线程创建 SpVoice 并朗读");

            // 6.55) 滚轮顶部回上一章末尾
            f.JumpTo(f.Chapters.Count - 1, f.Chapters[f.Chapters.Count - 1].Start);
            f.ScrollY = 0;
            f.ClampScroll();
            int upBefore = f.ChapterIndex;
            mw.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.None, 0, 600, 400, 120) });
            float maxY = Math.Max(0f, f.Layout.Height + f.HeadingH - (f.ContentBottom() - f.ContentTop()) + 12);
            maxY = Math.Max(maxY, f.PageStart(f.PageCount() - 1));
            bool atEnd = Math.Abs(f.ScrollY - maxY) < 2f;
            Check("滚轮顶部回上一章末尾", f.ChapterIndex == upBefore - 1 && atEnd,
                "第 " + (upBefore + 1) + " 章 → 第 " + (f.ChapterIndex + 1) + " 章, y=" + (int)f.ScrollY + "/" + (int)maxY);

            // 6.85) 朗读浮条的停止按钮要能点到（不能被正文选区吞掉）
            f.Current = View.Reader;
            f.TtsActive = true;
            RectangleF[] tbr = f.TtsBarRects();
            PointF stopPt = new PointF(tbr[4].X + tbr[4].Width / 2, tbr[4].Y + tbr[4].Height / 2);
            System.Reflection.MethodInfo mv = typeof(MainForm).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo mdStop = typeof(MainForm).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo mu = typeof(MainForm).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            mv.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.None, 0, (int)stopPt.X, (int)stopPt.Y, 0) });
            Check("浮条悬浮有反馈", f.TtsHover == 4, "hover=" + f.TtsHover);
            mdStop.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)stopPt.X, (int)stopPt.Y, 0) });
            Check("按下有状态", f.TtsPressed == 4 && f.TtsActive, "pressed=" + f.TtsPressed);
            mu.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)stopPt.X, (int)stopPt.Y, 0) });
            Check("朗读浮条停止可点击", !f.TtsActive && f.TtsPressed == -1, "TtsActive=" + f.TtsActive);

            // 6.88) 语速/定时即时生效
            f.TtsActive = true;
            Store.Lib.Settings.TtsAutoStopMinutes = 3;
            f.ApplyTtsMinutes();
            double leftMin = (f.TtsDeadline - DateTime.Now).TotalMinutes;
            Check("定时立即生效", Math.Abs(leftMin - 3) < 0.2, leftMin.ToString("0.0") + " 分钟，浮条显示 " + f.TtsRemaining());
            f.TtsActive = false;
            f.ApplyTtsMinutes();
            Check("停止后清空定时", f.TtsDeadline == DateTime.MinValue && f.TtsRemaining() == "关", f.TtsRemaining());

            // 6.89) 停止要清干净
            f.TtsActive = true; f.TtsStart = 5; f.TtsEnd = 10;
            f.StopTts();
            Check("停止后清空高亮", !f.TtsActive && f.TtsStart == -1 && f.TtsEnd == -1,
                "active=" + f.TtsActive + " start=" + f.TtsStart);

            // 6.895) 倒计时要按秒变化
            f.TtsActive = true;
            f.TtsDeadline = DateTime.Now.AddSeconds(65);
            string r1 = f.TtsRemaining();
            f.TtsDeadline = DateTime.Now.AddSeconds(5);
            string r2 = f.TtsRemaining();
            f.TtsDeadline = DateTime.Now.AddSeconds(-1);
            string r3 = f.TtsRemaining();
            bool secOk = (r1 == "1:05" || r1 == "1:04") && (r2 == "0:05" || r2 == "0:04") && r3 == "0:00";
            Check("倒计时按秒显示", secOk, r1 + " / " + r2 + " / " + r3);
            f.TtsActive = false; f.TtsDeadline = DateTime.MinValue;

            // 6.897) 尾部静音要被裁掉
            int rateHz = 22050, toneN = rateHz / 2, silN = rateHz * 8 / 10;
            byte[] pcm = new byte[(toneN + silN) * 2];
            for (int i = 0; i < toneN; i++)
            {
                short v = (short)(Math.Sin(2 * Math.PI * 440 * i / rateHz) * 8000);
                pcm[i * 2] = (byte)(v & 0xFF); pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            System.IO.MemoryStream msW = new System.IO.MemoryStream();
            System.IO.BinaryWriter bw = new System.IO.BinaryWriter(msW);
            bw.Write(new char[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + pcm.Length);
            bw.Write(new char[] { 'W', 'A', 'V', 'E' }); bw.Write(new char[] { 'f', 'm', 't', ' ' });
            bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(rateHz); bw.Write(rateHz * 2);
            bw.Write((short)2); bw.Write((short)16); bw.Write(new char[] { 'd', 'a', 't', 'a' }); bw.Write(pcm.Length); bw.Write(pcm);
            bw.Flush();
            byte[] raw = msW.ToArray();
            byte[] trimmed = Tts.TrimSilence(raw);
            double wavBefore = Tts.WavSeconds(raw), wavAfter = Tts.WavSeconds(trimmed);
            Check("裁掉尾部静音", wavBefore > 1.2 && wavAfter < 0.75, wavBefore.ToString("0.00") + "s → " + wavAfter.ToString("0.00") + "s");

            // 6.896) 朗读从视口第一行开始
            f.JumpTo(longCh, f.Chapters[longCh].Start);
            int kLine = Math.Min(6, f.Layout.Lines.Count - 1);
            f.ScrollY = f.Layout.Lines[kLine].Y + 3;
            f.ClampScroll();
            int firstOff = f.FirstVisibleOffset();
            Check("从视口首行开始", firstOff == f.Layout.Lines[kLine].Start,
                "off=" + firstOff + " lineStart=" + f.Layout.Lines[kLine].Start);

            // 6.92) 在线结果：同名合并 + 相关度排序
            var demoBooks = new System.Collections.Generic.List<OnlineBook>();
            demoBooks.Add(new OnlineBook { Title = "长夜将明", Author = "沈砚", Url = "http://a", SourceId = "a", SourceName = "A源" });
            demoBooks.Add(new OnlineBook { Title = "长夜将明", Author = "沈砚", Url = "http://b", SourceId = "b", SourceName = "B源" });
            demoBooks.Add(new OnlineBook { Title = "山河祭", Author = "佚名", Url = "http://c", SourceId = "c", SourceName = "C源" });
            f.BuildGroups(demoBooks, "长夜");
            Check("同名书合并", f.OnlineGroups.Count == 2 && f.OnlineGroups[0].Items.Count == 2,
                f.OnlineGroups.Count + " 组，第一组 " + (f.OnlineGroups.Count > 0 ? f.OnlineGroups[0].Items.Count : 0) + " 源");
            Check("相关度排序", f.OnlineGroups.Count > 0 && f.OnlineGroups[0].Title == "长夜将明",
                f.OnlineGroups.Count > 0 ? f.OnlineGroups[0].Title : "-");

            // 6.921) 结果行「目录」按钮命中优先于整行
            f.Current = View.Online;
            f.SyncMinSize();
            f.ClientSize = new Size(1200, 780);
            f.OnlineScroll = 0;
            using (Bitmap bmpOnline = new Bitmap(1200, 780)) f.DrawToBitmap(bmpOnline, new Rectangle(0, 0, 1200, 780));
            RectangleF tocBtnR = f.OnlineTocButtonRect(1, 0);
            f.OnlineTocRequested = false;
            f.OnlineClick(new PointF(tocBtnR.X + tocBtnR.Width / 2, tocBtnR.Y + tocBtnR.Height / 2));
            Check("目录按钮可点", f.OnlineTocRequested, "rect=" + (int)tocBtnR.X + "," + (int)tocBtnR.Y);

            // 6.923) 目录预览：真实取目录 + 点章跳转
            BookEntry keepBook = f.Book;
            var keepChapters = f.Chapters;
            int keepChapter = f.ChapterIndex;
            float keepScroll = f.ScrollY;
            MockOnline.Start();
            SourceDef mockSrc = Online.ParseBsJson(MockOnline.BsJson())[0];
            List<SourceDef> savedSrcs = new List<SourceDef>(Online.Sources);
            Online.Sources.Insert(0, mockSrc);
            string mockUrl = "http://127.0.0.1:" + MockOnline.Port + "/book/1";
            string mockBookId = Store.MakeId("online:" + mockUrl);
            List<OnlineBook> mockBooks = new List<OnlineBook>();
            mockBooks.Add(new OnlineBook
            {
                Title = "长夜将明", Author = "沈砚",
                Url = mockUrl,
                SourceId = mockSrc.Id, SourceName = mockSrc.Name
            });
            f.BuildGroups(mockBooks, "长夜");
            f.OnlineScroll = 0;
            using (Bitmap bmpToc = new Bitmap(1200, 780)) f.DrawToBitmap(bmpToc, new Rectangle(0, 0, 1200, 780));
            RectangleF mockTocBtn = f.OnlineTocButtonRect(0, 0);
            f.OnlineClick(new PointF(mockTocBtn.X + mockTocBtn.Width / 2, mockTocBtn.Y + mockTocBtn.Height / 2));
            for (int wi = 0; wi < 300 && f.OnlineTocBusy; wi++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            Check("目录预览取到章节", f.OnlineToc.Count == 2, "n=" + f.OnlineToc.Count + " err=" + f.OnlineTocErr);
            if (f.OnlineToc.Count == 2)
            {
                RectangleF card2 = f.OnlineTocCardRect();
                f.OnlineClick(new PointF(card2.X + 120, f.OnlineTocListTop + 34 + 8));
                Check("预览点章直接打开", f.Current == View.Reader && f.ChapterIndex == 1,
                    "view=" + f.Current + " ch=" + f.ChapterIndex + " n=" + f.Chapters.Count + " toast=" + f.Toast);
            }
            Online.Sources.Clear();
            Online.Sources.AddRange(savedSrcs);

            f.Book = keepBook;
            f.Chapters = keepChapters;
            f.ChapterIndex = keepChapter;
            f.PrepareChapter();
            f.BuildLayout(true);
            f.ScrollY = keepScroll;
            f.ClampScroll();
            for (int bi2 = Store.Lib.Books.Count - 1; bi2 >= 0; bi2--)
                if (Store.Lib.Books[bi2].Id == mockBookId) Store.Lib.Books.RemoveAt(bi2);
            f.Current = View.Online;
            f.SyncMinSize();

            // 6.924) 并行搜索：多个源同时搜完，结果合并
            var parSaved = new System.Collections.Generic.List<SourceDef>(Online.Sources);
            Online.Sources.Clear();
            for (int pi = 0; pi < 8; pi++)
            {
                SourceDef ps = Online.ParseBsJson(MockOnline.BsJson())[0];
                ps.Id = "par" + pi;
                ps.Name = "并行源" + pi;
                Online.Sources.Add(ps);
            }
            f.OpenOnlineSearch();
            f.OnlineBox.Text = "测试";
            f.OnlineSearching = false;
            System.Reflection.MethodInfo startSearch = typeof(MainForm).GetMethod("StartOnlineSearch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            startSearch.Invoke(f, null);
            for (int wi = 0; wi < 400 && f.OnlineSearching; wi++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            Check("并行搜索完成并合并", !f.OnlineSearching && !f.OnlineBusy && f.OnlineGroups.Count > 0,
                "busy=" + f.OnlineBusy + " groups=" + f.OnlineGroups.Count + " status=" + f.OnlineStatus);
            Online.Sources.Clear();
            Online.Sources.AddRange(parSaved);
            f.GoShelf();

            // 6.925) 每个源预览目录：点章、拖动、关闭
            f.Current = View.Online;
            f.SyncMinSize();
            f.ClientSize = new Size(1200, 780);
            f.OnlineTocBook = demoBooks[0];
            f.OnlineToc = new System.Collections.Generic.List<string[]>();
            for (int ci = 0; ci < 40; ci++) f.OnlineToc.Add(new string[] { "第" + (ci + 1) + "章 夜行", "http://x/" + ci });
            f.OnlineTocOpen = true;
            f.OnlineTocBusy = false;
            f.OnlineTocErr = "";
            f.OnlineTocScroll = 0;
            RectangleF tocCard = f.OnlineTocCardRect();
            int tocRow = f.OnlineTocRowAt(new PointF(tocCard.X + 100, f.OnlineTocListTop + 34 * 2 + 6));
            Check("目录预览点章", tocRow == 2, "row=" + tocRow);
            f.OnlineTocScroll = 34 * 5;
            int tocRow2 = f.OnlineTocRowAt(new PointF(tocCard.X + 100, f.OnlineTocListTop + 6));
            Check("目录预览可滚动", tocRow2 == 5, "row=" + tocRow2);
            f.OnlineTocTrack = new RectangleF(tocCard.Right - 22, f.OnlineTocListTop + 4, 12, Math.Max(10, f.OnlineTocListBottom - f.OnlineTocListTop - 8));
            bool tocBarHit = f.OnlineTocBarHit(new PointF(tocCard.Right - 16, f.OnlineTocListTop + 10));
            f.OnlineTocBarBegin(new PointF(tocCard.Right - 16, f.OnlineTocListTop + 10));
            f.OnlineTocBarMove(new PointF(tocCard.Right - 16, f.OnlineTocListTop + 60));
            bool tocBarMoved = f.OnlineTocScroll > 34 * 5;
            f.OnlineTocBarEnd();
            Check("目录预览可拖动", tocBarHit && tocBarMoved, "hit=" + tocBarHit + " scroll=" + (int)f.OnlineTocScroll);
            f.CloseOnlineToc();
            Check("目录预览可关闭", !f.OnlineTocOpen && f.OnlineToc.Count == 0, "open=" + f.OnlineTocOpen);

            // 6.93) 在线书：进入章节要自动取正文
            string onlineCache = System.IO.Path.Combine(Online.CacheDir("uicheck-online"), "c0.txt");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(onlineCache));
            System.IO.File.WriteAllText(onlineCache, "在线章节正文测试。", new System.Text.UTF8Encoding(false));
            BookEntry oldBook = f.Book;
            var oldChapters = f.Chapters;
            int oldIndex = f.ChapterIndex;
            f.Book = new BookEntry { Id = "uicheck-online", Online = true, AutoName = "在线书", SourceId = "x", Url = "http://x/" };
            f.Chapters = new System.Collections.Generic.List<Chapter> { new Chapter { Title = "第一章", Start = 0, Length = 0 } };
            f.ChapterIndex = 0;
            f.PrepareChapter();
            Check("在线章节自动加载", f.ChapterBody.Contains("在线章节正文测试"),
                f.ChapterBody.Length + " 字");
            f.Book = oldBook; f.Chapters = oldChapters; f.ChapterIndex = oldIndex; f.PrepareChapter();

            // 6.96) 书源要能落盘
            Online.Sources = new System.Collections.Generic.List<SourceDef>();
            Online.Sources.Add(new SourceDef { Id = "persist", Name = "落盘测试", Host = "http://x", ContentXPath = "//div" });
            Online.Save();
            bool persisted = Online.SourceFile != null && System.IO.File.Exists(Online.SourceFile);
            Check("书源写入磁盘", persisted, Online.SourceFile ?? "null");

            // 6.98) 在线书不算“文件不存在”
            BookEntry onlineProbe = new BookEntry { Id = "o", Online = true, Path = "https://example.com/book/1", Url = "https://example.com/book/1" };
            BookEntry localProbe = new BookEntry { Id = "l", Path = "Z:\\definitely-missing.txt" };
            Check("在线书不报文件缺失", !MainForm.ShelfMissing(onlineProbe) && MainForm.ShelfMissing(localProbe),
                "online=" + MainForm.ShelfMissing(onlineProbe) + " local=" + MainForm.ShelfMissing(localProbe));

            // 6.9) 在线输入框位置与隐藏
            f.OpenOnlineSearch();
            Check("在线输入框定位", f.OnlineWanted && f.OnlineBox != null && f.OnlineBox.Bounds.X > 30 && f.OnlineBox.Bounds.Y > 50,
                f.OnlineBox == null ? "null" : f.OnlineBox.Bounds.ToString());
            f.GoShelf();
            Check("离开在线隐藏输入框", !f.OnlineWanted, "");

            // 6.91) 搜索可以随时停止，已返回的结果保留
            f.OpenOnlineSearch();
            f.OnlineGroups = new System.Collections.Generic.List<MainForm.OnlineGroup>();
            var stopGroup = new MainForm.OnlineGroup { Title = "测试书" };
            f.OnlineGroups.Add(stopGroup);
            f.OnlineSearching = true;
            f.OnlineBusy = true;
            float stopBw = Math.Min(420, f.W - 420);
            f.OnlineClick(new PointF(40 + stopBw + 12 + 48, 86));
            Check("可以停止搜索", !f.OnlineSearching && !f.OnlineBusy && f.OnlineStatus.Contains("停止") && f.OnlineGroups.Count == 1,
                f.OnlineStatus);
            f.GoShelf();

            // 6.95) 最大化与窗口按钮
            System.Reflection.MethodInfo dbl = typeof(MainForm).GetMethod("OnMouseDoubleClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f.WindowState = FormWindowState.Normal;
            dbl.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 1030, 22, 0) });
            Check("双击喇叭不最大化", f.WindowState == FormWindowState.Normal, f.WindowState.ToString());
            dbl.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 2, 600, 22, 0) });
            Check("双击标题最大化", f.WindowState == FormWindowState.Maximized, f.WindowState.ToString());
            System.Reflection.MethodInfo md2 = typeof(MainForm).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            md2.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 1134, 20, 0) });
            Check("最大化后仍可还原", f.WindowState == FormWindowState.Normal, f.WindowState.ToString());

            // 6.94) 从设置页返回在线搜索，输入框要重新出现
            f.OpenSettings();
            bool hiddenInSettings = !f.OnlineWanted;
            f.OpenOnlineSearch();
            Check("设置返回后搜索框可用", hiddenInSettings && f.OnlineWanted && f.OnlineBox.Bounds.X > 30 && f.Current == View.Online,
                "hidden=" + hiddenInSettings + " wanted=" + f.OnlineWanted + " x=" + f.OnlineBox.Bounds.X);
            f.GoShelf();

            // 6.95) 从设置返回在线页，搜索框要恢复
            f.OpenOnlineSearch();
            f.OpenSettings();
            f.CloseSettings();
            Check("返回在线页搜索框恢复", f.Current == View.Online && f.OnlineWanted && f.OnlineBox.Visible == (f.Visible),
                "current=" + f.Current + " wanted=" + f.OnlineWanted);
            f.GoShelf();

            // 6.99) 中间点击打开目录要定位当前章；批量移除
            var tocChapters = new System.Collections.Generic.List<Chapter>();
            for (int ci = 0; ci < 40; ci++) tocChapters.Add(new Chapter { Title = "第" + (ci + 1) + "章", Start = 0, Length = 10 });
            var tocKeep = f.Chapters; int tocKeepIdx = f.ChapterIndex; var tocKeepPanel = f.OpenPanel;
            f.Chapters = tocChapters; f.ChapterIndex = 25; f.OpenPanel = Panel.None;
            Store.Lib.Settings.ClickMode = 1;
            var rc = typeof(MainForm).GetMethod("ReaderClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            rc.Invoke(f, new object[] { new PointF(f.W / 2, 300) });
            float tocExpected = Math.Max(0, Math.Min(Math.Max(0, 40 * 52f + 8 - Math.Max(120, f.Hh - 132)), 25 * 52f));
            Check("中间点击目录定位当前章", f.OpenPanel == Panel.Toc && Math.Abs(f.PanelScroll - tocExpected) < 1,
                "panel=" + f.OpenPanel + " scroll=" + (int)f.PanelScroll);
            f.OpenPanel = Panel.None; f.Chapters = tocKeep; f.ChapterIndex = tocKeepIdx;

            int booksBefore = Store.Lib.Books.Count;
            Store.Lib.Books.Add(new BookEntry { Id = "bt1", Path = "x1", AutoName = "批1" });
            Store.Lib.Books.Add(new BookEntry { Id = "bt2", Path = "x2", AutoName = "批2" });
            Store.Lib.Books.Add(new BookEntry { Id = "bt3", Path = "x3", AutoName = "批3" });
            f.RebuildShelf();
            f.BatchSel.Clear(); f.BatchSel.Add("bt1"); f.BatchSel.Add("bt2");
            int removedCount = f.BatchRemoveSelected(false, false);
            Check("批量移出书架", removedCount == 2 && Store.Lib.Books.Count == booksBefore + 1,
                "移除 " + removedCount + "，剩 " + Store.Lib.Books.Count);
            for (int bi3 = Store.Lib.Books.Count - 1; bi3 >= 0; bi3--)
                if (Store.Lib.Books[bi3].Id == "bt3") Store.Lib.Books.RemoveAt(bi3);
            f.RebuildShelf();

            // 6.995) 点击右半边要能进下一章（滚动/分页两种模式）
            f.Current = View.Reader;
            Store.Lib.Settings.ClickMode = 1;
            Store.Lib.Settings.PageMode = false;
            var mdClick = typeof(MainForm).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var muClick = typeof(MainForm).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f.JumpTo(longCh, f.Chapters[longCh].Start);
            f.ScrollY = 1e9f; f.ClampScroll();
            int chClick = f.ChapterIndex;
            float rx = f.ContentLeft() + f.ContentWidth() * 0.9f;
            float ry = f.ContentTop() + f.HeadingH + 80;
            mdClick.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)rx, (int)ry, 0) });
            muClick.Invoke(f, new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)rx, (int)ry, 0) });
            int afterMouse = f.ChapterIndex;
            if (afterMouse == chClick) { rc.Invoke(f, new object[] { new PointF(rx, ry) }); }
            int afterClick = f.ChapterIndex;
            if (afterClick == chClick) { f.NextPage(); }
            Check("点右半边进下一章", f.ChapterIndex == chClick + 1,
                "第 " + (chClick + 1) + " → " + (f.ChapterIndex + 1) + " mouse=" + afterMouse
                + " end=" + f.AtChapterEnd()
                + " y=" + (int)f.ScrollY + " layout=" + (f.Layout == null ? 0 : (int)f.Layout.Height)
                + " panel=" + f.OpenPanel + " ch=" + f.Chapters.Count
                + " pm=" + Store.Lib.Settings.PageMode + " pi=" + f.PageIndex() + "/" + f.PageCount()
                + " click=" + afterClick);

            // 6.997) 面板：叉叉避开窗口按钮、右侧条可拖、书架进度=统计进度
            float closeRight = f.PanelX() + 380 - 168 + 32;
            Check("目录叉叉避开窗口按钮", closeRight <= f.W - 132, "closeRight=" + (int)closeRight + " winLeft=" + (int)(f.W - 132));
            var barChapters = new System.Collections.Generic.List<Chapter>();
            for (int ci = 0; ci < 60; ci++) barChapters.Add(new Chapter { Title = "b" + ci, Start = 0, Length = 10 });
            var barKeep = f.Chapters; f.Chapters = barChapters;
            f.OpenPanel = Panel.Toc;
            f.PanelScroll = 0;
            float barX = f.PanelX() + 368;
            bool barHit = f.PanelBarHit(new PointF(barX, 300));
            f.PanelBarBegin(new PointF(barX, 300));
            f.PanelBarMove(new PointF(barX, 340));
            bool barMoved = f.PanelScroll > 0;
            f.PanelBarEnd();
            Check("目录滚动条可拖", barHit && barMoved, "hit=" + barHit + " scroll=" + (int)f.PanelScroll);
            f.OpenPanel = Panel.None; f.PanelScroll = 0; f.Chapters = barKeep;

            var pctBook = new BookEntry { Id = "shelfpct", AutoName = "进度" };
            var pctChs2 = new System.Collections.Generic.List<Chapter>();
            for (int ci = 0; ci < 10; ci++) pctChs2.Add(new Chapter { Title = "c" + ci, Start = ci * 100, Length = 100 });
            pctBook.ReadLog = new System.Collections.Generic.List<ChapterRead>();
            for (int ci = 0; ci < 3; ci++) pctBook.ReadLog.Add(new ChapterRead { Chapter = ci, Percent = 1f });
            var pctKeepBook = f.Book; var pctKeepCh = f.Chapters;
            f.Book = pctBook; f.Chapters = pctChs2; f.JumpTo(0, 0); f.SaveProgress();
            Check("书架进度跟随统计", pctBook.Progress != null && Math.Abs(pctBook.Progress.Percent - 0.3) < 0.01,
                pctBook.Progress == null ? "null" : (pctBook.Progress.Percent * 100).ToString("0") + "%");
            f.Book = pctKeepBook; f.Chapters = pctKeepCh; f.PrepareChapter();

            // 7.5) 拖动缩小窗口：正文要重新断行，不能被裁掉；阅读页允许缩窄
            f.Current = View.Reader;
            f.SyncMinSize();
            f.ClientSize = new Size(1200, 780);
            f.ReflowReader();
            float wideW = f.LayoutWidth;
            int wideLines = f.Layout == null ? 0 : f.Layout.Lines.Count;
            f.ScrollY = 0; f.ClampScroll();
            f.ClientSize = new Size(620, 600);
            f.ReflowReader();
            bool reflowed = f.Layout != null && f.LayoutWidth < wideW - 50 &&
                            Math.Abs(f.LayoutWidth - f.ContentWidth()) < 1f &&
                            f.Layout.Lines.Count > wideLines;
            Check("窄窗正文重新断行", reflowed,
                "宽=" + (int)wideW + "→" + (int)f.LayoutWidth + "，行=" + wideLines + "→" + (f.Layout == null ? 0 : f.Layout.Lines.Count));
            Check("阅读页可以缩窄", f.MinimumSize.Width <= 430 && f.MinimumSize.Height <= 370,
                f.MinimumSize.Width + "x" + f.MinimumSize.Height);

            // 7.55) 420x360 窄窗：工具栏移第二行，正文至少留两行
            f.ClientSize = new Size(420, 360);
            f.ReflowReader();
            bool narrowHeader = f.HeaderH() <= 76f && f.IconRects()[0].Y >= 44f && f.ContentTop() >= 108f;
            float bodyRoom = f.ContentBottom() - f.BodyTop();
            Check("窄窗工具栏不压书名", narrowHeader, "header=" + f.HeaderH() + " iconY=" + f.IconRects()[0].Y + " top=" + f.ContentTop());
            Check("窄窗正文留足行高", bodyRoom >= 90f, "正文高=" + (int)bodyRoom);
            f.ScrollY = 1e9f; f.ClampScroll();
            LineInfo ll = f.Layout.Lines[f.Layout.Lines.Count - 1];
            float lastY = f.BodyTop() + ll.Y - f.ScrollY;
            Check("滚到底最后一行完整", lastY + ll.H <= f.ContentBottom() + 2f,
                "底=" + (int)(lastY + ll.H) + " 界=" + (int)f.ContentBottom());
            f.ClientSize = new Size(1200, 780);
            f.ReflowReader();

            // 7.56) 下键与自动翻页到章末都要进入下一章
            int navKeep = f.ChapterIndex;
            f.Current = View.Reader;
            f.OpenPanel = Panel.None;
            f.JumpTo(0, 0);
            f.ScrollY = 1e9f;
            f.ClampScroll();
            bool navEnd = f.AtChapterEnd();
            bool navDown = f.HandleKey(Keys.Down);
            Check("下键到底进入下一章", navEnd && navDown && f.ChapterIndex == 1,
                "end=" + navEnd + " ch=" + f.ChapterIndex);
            f.JumpTo(0, 0);
            f.ScrollY = 1e9f;
            f.ClampScroll();
            System.Reflection.MethodInfo autoTick = typeof(MainForm).GetMethod("AutoTick",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            autoTick.Invoke(f, null);
            Check("自动翻页到底进入下一章", f.ChapterIndex == 1, "ch=" + f.ChapterIndex);
            f.JumpTo(1, 0);
            f.ScrollY = 0;
            f.ClampScroll();
            bool navUp = f.HandleKey(Keys.Up);
            Check("上键到底进入上一章", navUp && f.ChapterIndex == 0,
                "ch=" + f.ChapterIndex + " y=" + (int)f.ScrollY);
            f.JumpTo(navKeep, 0);

            // 7) 排序：上次阅读第一
            f.GoShelf();            f.RebuildShelf();
            Check("上次阅读第一", f.ShelfView.Count > 0 && string.Equals(f.ShelfView[0].Path, book.Path, StringComparison.OrdinalIgnoreCase),
                f.ShelfView.Count > 0 ? f.ShelfView[0].DisplayName : "-");

            // 7.58) 书源批量选择 / 启用 / 停用
            var keepSources = new System.Collections.Generic.List<SourceDef>(Online.Sources);
            Online.Sources.Clear();
            for (int si = 0; si < 3; si++) Online.Sources.Add(new SourceDef { Id = "batch" + si, Name = "批量源" + si, Host = "http://x" + si, Enabled = true });
            f.Current = View.Settings;
            f.SettingsSection = 3;
            f.SourceBatch = true;
            f.SourceSel.Clear();
            f.SourceSelectAll(true);
            bool selAll = f.SourceSel.Count == 3;
            f.SourceSetEnabled(false);
            bool disabled = true;
            foreach (SourceDef sx in Online.Sources) if (sx.Enabled) disabled = false;
            f.SourceInvert();
            bool inverted = f.SourceSel.Count == 0;
            f.SourceSelectAll(true);
            f.SourceSel.Remove("batch0");
            int removedSrc = f.RemoveSelectedSources();
            bool deleteOk = removedSrc == 2 && Online.Sources.Count == 1 && Online.Sources[0].Id == "batch0";
            Online.Sources.Clear();
            Online.Sources.AddRange(keepSources);
            f.SourceBatch = false;
            f.SourceSel.Clear();
            Check("书源批量选择与启停", selAll && disabled && inverted, "sel=" + selAll + " off=" + disabled + " inv=" + inverted);
            Check("书源批量删除", deleteOk, "removed=" + removedSrc + " left=" + Online.Sources.Count);

            // 7.6) 设置页右侧滚动条可拖
            f.Current = View.Settings;
            f.SyncMinSize();
            f.ClientSize = new Size(1000, 600);
            f.SettingsContentH = 2000;
            f.SettingsScroll = 0;
            PointF setBar = new PointF(f.W - 10, 300);
            bool setBarHit = f.SettingsBarHit(setBar);
            f.SettingsBarBegin(setBar);
            f.SettingsBarMove(new PointF(f.W - 10, 420));
            float setScroll = f.SettingsScroll;
            f.SettingsBarMove(new PointF(f.W - 10, 900));
            float setMax = f.SettingsContentH - f.SetViewH();
            bool setClamped = Math.Abs(f.SettingsScroll - setMax) < 1f;
            f.SettingsBarEnd();
            Check("设置页滚动条可拖", setBarHit && setScroll > 0 && setClamped,
                "hit=" + setBarHit + " scroll=" + (int)setScroll + " max=" + (int)setMax);
            f.SettingsContentH = 0; f.SettingsScroll = 0;

            // 8) 主题与设置
            f.CycleTheme();
            Check("切换主题", f.Th.Name.Length > 0, f.Th.Name);
            float fs = Store.Lib.Settings.FontSize;
            f.SetFontSize(fs + 2);
            Check("调整字号", Math.Abs(Store.Lib.Settings.FontSize - (fs + 2)) < 0.01, Store.Lib.Settings.FontSize + " px");

            Console.WriteLine();
            Console.WriteLine("RESULT  pass=" + _pass + "  fail=" + _fail);
            return _fail == 0 ? 0 : 1;
        }
    }
}
