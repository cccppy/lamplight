using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public string ChapterBody = "";
        public int ChapterBodySkip;

        // ============================================================ 章节准备
        public void PrepareChapter()
        {
            ChapterBody = "";
            ChapterBodySkip = 0;
            if (Chapters.Count == 0) return;

            // 在线书：正文按章现取（本地缓存优先）
            if (Book != null && Book.Online)
            {
                string err;
                string text = Online.GetChapter(Book, ChapterIndex, out err);
                if (string.IsNullOrEmpty(text))
                {
                    ChapterBody = "";
                    if (!string.IsNullOrEmpty(err)) ShowToast(err);
                }
                else
                {
                    Chapters[ChapterIndex].Length = text.Length;
                    ChapterBody = Compress(text);
                }
                return;
            }

            if (BookText.Length == 0) return;
            Chapter c = Chapters[Math.Max(0, Math.Min(ChapterIndex, Chapters.Count - 1))];
            int start = Math.Max(0, Math.Min(c.Start, BookText.Length));
            int len = Math.Max(0, Math.Min(c.Length, BookText.Length - start));
            string raw = BookText.Substring(start, len);

            int skip = 0;
            while (skip < raw.Length && (raw[skip] == '\r' || raw[skip] == '\n' || raw[skip] == ' ' || raw[skip] == '\u3000')) skip++;
            string t = raw.Substring(skip);

            int nl = t.IndexOf('\n');
            if (nl > 0)
            {
                string first = t.Substring(0, nl).Trim();
                string title = c.Title == null ? "" : c.Title.Trim();
                if (first.Length > 0 && first.Length <= 80 &&
                    (first == title || (title.Length > 0 && first.Replace(" ", "").EndsWith(title))))
                {
                    skip += nl + 1;
                    t = t.Substring(nl + 1);
                }
            }
            ChapterBodySkip = skip;
            ChapterBody = Compress(t);
        }

        static string Compress(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = s.Split('\n');
            List<string> keep = new List<string>();
            int blank = 0;
            bool compress = Store.Lib.Settings.CompressBlank;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd(' ', '\t', '\u3000');
                if (line.Trim().Length == 0)
                {
                    blank++;
                    if (compress && blank > 1) continue;
                    keep.Add("");
                }
                else
                {
                    blank = 0;
                    keep.Add(line);
                }
            }
            while (keep.Count > 0 && keep[keep.Count - 1].Length == 0) keep.RemoveAt(keep.Count - 1);
            return string.Join("\n", keep.ToArray());
        }

        // ============================================================ 进度
        public float ChapterPercent()
        {
            if (Layout == null || Layout.Height <= 0) return 0;
            float viewH = ContentBottom() - ContentTop();
            float v = (ScrollY + viewH - HeadingH) / Layout.Height;
            return Math.Max(0, Math.Min(1, v));
        }

        public float ReadPercent(int chapter)
        {
            if (Book == null || Book.ReadLog == null) return 0;
            foreach (ChapterRead r in Book.ReadLog)
                if (r.Chapter == chapter) return Math.Max(0, Math.Min(1, r.Percent));
            return 0;
        }

        public void TrackChapter()
        {
            if (Book == null || Chapters.Count == 0) return;
            if (Book.ReadLog == null) Book.ReadLog = new List<ChapterRead>();
            float pct = ChapterPercent();
            if (pct >= 0.95f) pct = 1f;
            ChapterRead entry = null;
            foreach (ChapterRead r in Book.ReadLog) if (r.Chapter == ChapterIndex) { entry = r; break; }
            if (entry == null)
            {
                entry = new ChapterRead();
                entry.Chapter = ChapterIndex;
                Book.ReadLog.Add(entry);
            }
            if (pct > entry.Percent) entry.Percent = pct;
            else if (entry.Percent == 0 && pct > 0) entry.Percent = pct;
        }

        int TopOffset()
        {
            if (Layout == null || Layout.Lines.Count == 0) return 0;
            int li = TextDoc.LineAtY(Layout, Math.Max(0, ScrollY));
            return Layout.Lines[li].Start;
        }

        public void SaveProgress()
        {
            if (Book == null || Chapters.Count == 0 || Layout == null) return;
            TrackChapter();
            Progress pr = new Progress();
            pr.Chapter = ChapterIndex;
            pr.Offset = Chapters[ChapterIndex].Start + ChapterBodySkip + TopOffset();
            pr.Percent = StatsProgress();   // 与阅读统计一致：已读章节 / 总章节
            Book.Progress = pr;
            Book.LastRead = Store.Now();
            Store.Save();
        }

        public void JumpTo(int chapter, int offset)
        {
            if (Chapters.Count == 0) return;
            chapter = Math.Max(0, Math.Min(Chapters.Count - 1, chapter));
            ChapterIndex = chapter;
            BuildLayout();
            int rel = Math.Max(0, offset - Chapters[chapter].Start - ChapterBodySkip);
            if (ChapterBody.Length > 0) rel = Math.Min(rel, ChapterBody.Length - 1);
            int li = TextDoc.LineOfOffset(Layout, rel);
            ScrollY = Math.Max(0, Layout.Lines[li].Y - 40);

            ClampScroll();
            SelStart = SelEnd = -1;
            SaveProgress();
            Invalidate();
        }

        // ============================================================ 翻页
        List<float> _pageStarts;
        DocLayout _pageLayout;
        float _pageViewH = -1;

        float BodyViewH()
        {
            return Math.Max(40, ContentBottom() - (ContentTop() + HeadingH));
        }

        /// <summary>按整行边界切页：每页起点都对齐到某一行行首。</summary>
        List<float> GetPageStarts()
        {
            float vh = BodyViewH();
            if (_pageStarts != null && _pageLayout == Layout &&
                Math.Abs(_pageViewH - vh) < 0.5f && _pageLayout != null &&
                _pageStarts.Count > 0 &&
                (_pageStarts[_pageStarts.Count - 1] < Layout.Height + 1f || Layout.Lines.Count == 0))
                return _pageStarts;

            List<float> list = new List<float>();
            if (Layout == null || Layout.Lines.Count == 0)
            {
                list.Add(0f);
            }
            else
            {
                list.Add(0f);
                int i = 0;
                while (i < Layout.Lines.Count)
                {
                    float startY = Layout.Lines[i].Y;
                    float limit = startY + vh;
                    int j = i;
                    while (j < Layout.Lines.Count && Layout.Lines[j].Y + Layout.Lines[j].H <= limit + 0.6f) j++;
                    if (j >= Layout.Lines.Count) break;
                    if (j == i) j = i + 1;
                    list.Add(Layout.Lines[j].Y);
                    i = j;
                }
            }
            _pageStarts = list;
            _pageLayout = Layout;
            _pageViewH = vh;
            return list;
        }

        public int PageCount()
        {
            return GetPageStarts().Count;
        }

        public int PageIndex()
        {
            List<float> starts = GetPageStarts();
            int idx = 0;
            for (int i = 0; i < starts.Count; i++)
                if (starts[i] <= ScrollY + 0.6f) idx = i; else break;
            return Math.Max(0, Math.Min(starts.Count - 1, idx));
        }

        public float PageStart(int index)
        {
            List<float> starts = GetPageStarts();
            if (starts.Count == 0) return 0;
            return starts[Math.Max(0, Math.Min(starts.Count - 1, index))];
        }

        float PageStartFor(float y)
        {
            List<float> starts = GetPageStarts();
            float best = 0;
            foreach (float s in starts) if (s <= y + 0.6f) best = s; else break;
            return best;
        }

        public bool AtChapterEnd()
        {
            if (Layout == null) return true;
            float viewH = ContentBottom() - ContentTop();
            return ScrollY + viewH >= HeadingH + Layout.Height - 8;
        }

        public void NextPage()
        {
            if (Layout == null) return;
            int idx = PageIndex();
            if (idx >= PageCount() - 1)
            {
                if (ChapterIndex < Chapters.Count - 1) GoChapter(ChapterIndex + 1);
                return;
            }
            ScrollY = PageStart(idx + 1);
            ClampScroll();
            TrackChapter();
            SaveProgress();
            Invalidate();
        }

        public void PrevPage()
        {
            if (Layout == null) return;
            int idx = PageIndex();
            if (ScrollY <= 1f || idx <= 0)
            {
                if (ChapterIndex > 0)
                {
                    GoChapter(ChapterIndex - 1);
                    ScrollY = 1e9f;
                    ClampScroll();
                    TrackChapter();
                    SaveProgress();
                    Invalidate();
                }
                return;
            }
            ScrollY = PageStart(idx - 1);
            ClampScroll();
            SaveProgress();
            Invalidate();
        }

        public void ReloadChapter()
        {
            float keep = ScrollY;
            if (Book != null && Book.Online)
            {
                string f = Path.Combine(Online.CacheDir(Book.Id), "c" + ChapterIndex + ".txt");
                try { if (File.Exists(f)) File.Delete(f); } catch { }
                Chapters[ChapterIndex].Length = 0;
            }
            BuildLayout();
            ScrollY = keep;
            ClampScroll();
            ShowToast("已重新加载本章");
            Invalidate();
        }

        // ============================================================ 选择
        public int OffsetAt(PointF m)
        {
            if (Layout == null || Layout.Lines.Count == 0) return 0;
            float y = m.Y - (ContentTop() + HeadingH) + ScrollY;
            int li = TextDoc.LineAtY(Layout, y);
            LineInfo ln = Layout.Lines[li];
            using (Font f = NewBodyFont())
            {
                float indent = (ln.ParaStart && Store.Lib.Settings.Indent) ? f.Size * 2f : 0f;
                float x = Math.Max(0, m.X - (ContentLeft() + indent));
                float ls = Store.Lib.Settings.LetterSpacing;
                float acc = 0;
                int idx = 0;
                while (idx < ln.Len && ln.Start + idx < ChapterBody.Length)
                {
                    float cw = TextDoc.CharW(f, ChapterBody[ln.Start + idx]) + ls;
                    if (acc + cw / 2f >= x) break;
                    acc += cw;
                    idx++;
                }
                return ln.Start + idx;
            }
        }

        public void UpdateSelection(PointF m)
        {
            if (!Selecting) return;
            SelEnd = OffsetAt(m);
            if (SelStart < 0) SelStart = SelEnd;
            Invalidate();
        }

        public void CopySelection()
        {
            if (SelStart < 0 || SelEnd <= SelStart || ChapterBody.Length == 0) { ShowToast("先拖选一段文字"); return; }
            int a = Math.Max(0, Math.Min(SelStart, SelEnd));
            int b = Math.Min(ChapterBody.Length, Math.Max(SelStart, SelEnd));
            if (b <= a) return;
            try { Clipboard.SetText(ChapterBody.Substring(a, b - a)); ShowToast("已复制 " + (b - a) + " 字"); }
            catch { ShowToast("复制失败"); }
        }

        // ============================================================ 书签
        public void AddBookmark()
        {
            if (Book == null || Chapters.Count == 0 || Layout == null) return;
            if (Book.Bookmarks == null) Book.Bookmarks = new List<BookmarkItem>();
            int rel = TopOffset();
            int abs = Chapters[ChapterIndex].Start + ChapterBodySkip + rel;
            foreach (BookmarkItem b in Book.Bookmarks)
                if (b.Chapter == ChapterIndex && Math.Abs(b.Offset - abs) < 24) { ShowToast("这个位置已经有书签了"); return; }

            string excerpt = ChapterBody.Substring(rel, Math.Min(36, Math.Max(0, ChapterBody.Length - rel))).Replace("\n", " ").Trim();
            BookmarkItem item = new BookmarkItem();
            item.Chapter = ChapterIndex;
            item.Offset = abs;
            item.ChapterTitle = Chapters[ChapterIndex].Title;
            item.Excerpt = excerpt;
            item.Created = Store.Now();
            Book.Bookmarks.Add(item);
            Store.Save();
            ShowToast("已添加书签 · 第 " + (ChapterIndex + 1) + " 章 " + (int)(ChapterPercent() * 100) + "%");
            Invalidate();
        }

        // ============================================================ 背景
        public void ReloadBackground()
        {
            string path = Store.Lib.Settings.BgImage;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (BgCache != null) { BgCache.Dispose(); BgCache = null; }
                if (BgSoft != null) { BgSoft.Dispose(); BgSoft = null; }
                BgCachePath = "";
                return;
            }
            if (path == BgCachePath && BgCache != null) return;
            try
            {
                if (BgCache != null) { BgCache.Dispose(); BgCache = null; }
                if (BgSoft != null) { BgSoft.Dispose(); BgSoft = null; }
                BgCache = new Bitmap(path);
                BgCachePath = path;
                long sum = 0; int cnt = 0;
                int stepY = Math.Max(1, BgCache.Height / 24), stepX = Math.Max(1, BgCache.Width / 24);
                for (int y = 0; y < BgCache.Height; y += stepY)
                    for (int x = 0; x < BgCache.Width; x += stepX)
                    {
                        Color c = BgCache.GetPixel(x, y);
                        sum += (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                        cnt++;
                    }
                BgLuma = cnt > 0 ? (int)(sum / cnt) : 255;
                int tw = Math.Min(360, BgCache.Width);
                int th = Math.Max(1, BgCache.Height * tw / BgCache.Width);
                using (Bitmap small = new Bitmap(BgCache, tw, th)) BgSoft = new Bitmap(small);
            }
            catch
            {
                if (BgCache != null) { BgCache.Dispose(); BgCache = null; }
                if (BgSoft != null) { BgSoft.Dispose(); BgSoft = null; }
                BgCachePath = "";
            }
        }

        public static Color ParseHexColor(string s)
        {
            if (string.IsNullOrEmpty(s)) return Color.Empty;
            string t = s.TrimStart('#');
            try
            {
                if (t.Length == 6)
                    return Color.FromArgb(Convert.ToInt32(t.Substring(0, 2), 16),
                        Convert.ToInt32(t.Substring(2, 2), 16), Convert.ToInt32(t.Substring(4, 2), 16));
            }
            catch { }
            return Color.Empty;
        }

        void DrawBackground(Painter p, float w, float h)
        {
            ReloadBackground();
            if (BgCache == null) return;
            Bitmap img = BgSoft != null ? BgSoft : BgCache;
            int mode = Store.Lib.Settings.BgMode;
            if (mode == 1)
            {
                float tw = Math.Max(240, w * 0.45f);
                float th = tw * img.Height / img.Width;
                for (float y = 0; y < h; y += th)
                    for (float x = 0; x < w; x += tw)
                        p.G.DrawImage(img, x, y, tw, th);
            }
            else if (mode == 2)
            {
                float s = Math.Min(w / img.Width, h / img.Height);
                p.G.DrawImage(img, (w - img.Width * s) / 2, (h - img.Height * s) / 2, img.Width * s, img.Height * s);
            }
            else
            {
                float s = Math.Max(w / img.Width, h / img.Height);
                p.G.DrawImage(img, (w - img.Width * s) / 2, (h - img.Height * s) / 2, img.Width * s, img.Height * s);
            }
            int dim = Math.Max(0, Math.Min(85, Store.Lib.Settings.BgDim));
            if (dim > 0) p.Fill(Color.FromArgb(dim * 255 / 100, 0, 0, 0), new RectangleF(0, 0, w, h));
        }

        // ============================================================ 绘制
        public void DrawReader(Painter p, RectangleF area)
        {
            float w = area.Width, h = area.Height;
            p.Clear(Th.Window);
            DrawBackground(p, w, h);

            bool hasBg = BgCache != null;
            bool chrome = Store.Lib.Settings.ShowChrome;

            if (chrome)
            {
                p.Fill(hasBg ? Color.FromArgb(230, Th.Window) : Th.Window, new RectangleF(0, 0, w, 44));
                p.Line(Th.Line, 0, 44, w, 44, 1);

                if (HoverPanel == 10) p.FillRound(Th.Hover, BackRect(), 7);
                p.DrawIcon(Glyph.Back, new RectangleF(22, 14, 18, 16), Th.Muted, 1.5f);
                if (Book != null)
                {
                    p.TextMid(Book.DisplayName, p.F(12.5f, true), Th.Ink, 56, 22);
                    float tw = p.Width(Book.DisplayName, p.F(12.5f, true));
                    if (Chapters.Count > 0)
                    {
                        string ct = Chapters[ChapterIndex].Title;
                        float maxW = w - 300 - (56 + tw + 14);
                        p.TextMid(p.Truncate(ct, p.F(11), Math.Max(60, maxW)), p.F(11), Th.Faint, 56 + tw + 14, 22);
                    }
                }

                RectangleF[] ic = IconRects();
                Glyph[] kinds = { Glyph.Toc, Glyph.Bookmark, Glyph.Chart, Glyph.Speaker, Glyph.Gear };
                for (int i = 0; i < 5; i++)
                {
                    bool on = (i == 0 && OpenPanel == Panel.Toc) || (i == 1 && OpenPanel == Panel.Bookmarks)
                           || (i == 2 && OpenPanel == Panel.Stats) || (i == 3 && TtsActive);
                    if (HoverPanel == 100 + i) p.FillRound(Th.Hover, ic[i], 7);
                    p.DrawIcon(kinds[i], new RectangleF(ic[i].X + 8, ic[i].Y + 5, 17, 17),
                        on ? Th.Accent : Th.Muted, 1.4f);
                }

                double topPct = BookPercent(ChapterIndex, ChapterPercent());
                p.Fill(Th.Accent, new RectangleF(0, 44, (float)(w * topPct), 2));
            }
            else
            {
                p.Fill(Th.Accent, new RectangleF(0, 0, (float)(w * BookPercent(ChapterIndex, ChapterPercent())), 2));
            }

            if (Layout == null || Chapters.Count == 0)
            {
                p.TextMidCenter("这本书还没有内容", p.F(13), Th.Faint, w / 2, h / 2);
                return;
            }

            float left = ContentLeft(), width = ContentWidth();
            float bodyTop = ContentTop() + HeadingH;
            float bottom = ContentBottom();

            Color inkCol = Th.Ink, bodyCol = Th.Body, faintCol = Th.Faint;
            Color custom = ParseHexColor(Store.Lib.Settings.TextColor);
            bool hasCustom = custom != Color.Empty;
            if (hasCustom) { inkCol = custom; bodyCol = custom; }
            bool lightText = false;

            if (hasBg)
            {
                int alpha = Math.Max(0, Math.Min(100, Store.Lib.Settings.PanelOpacity)) * 255 / 100;
                lightText = alpha < 150 && BgLuma < 120;
                if (alpha > 0)
                {
                    RectangleF paper = new RectangleF(left - 46, ContentTop() - 18, width + 92, bottom - ContentTop() + 34);
                    p.Shadow(paper, 14, 60, 4, 6);
                    p.FillRound(Color.FromArgb(alpha, lightText ? Color.FromArgb(24, 22, 20) : Th.Paper), paper, 14);
                }
                if (lightText && !hasCustom)
                {
                    inkCol = Color.FromArgb(240, 235, 226);
                    bodyCol = Color.FromArgb(222, 216, 205);
                    faintCol = Color.FromArgb(150, 143, 132);
                }
            }

            float hy = ContentTop() + 8;
            p.Text(Chapters[ChapterIndex].Title, p.F(27, false, true), inkCol, left, hy);
            string meta = "本章 " + ChapterBody.Length + " 字 · 第 " + (ChapterIndex + 1) + " / " + Chapters.Count + " 章";
            if (Book != null && Book.ReadLog != null)
            {
                float rp = ReadPercent(ChapterIndex);
                meta += rp >= 0.95f ? " · 已读" : (rp > 0 ? " · 在读 " + (int)(rp * 100) + "%" : " · 未读");
            }
            p.Text(meta, p.F(10.5f), lightText ? faintCol : Th.Muted, left + 2, hy + 44);
            p.Line(lightText ? faintCol : Th.Line, left, hy + 68, left + 46, hy + 68, 2);

            Region oldClip = p.G.Clip;
            p.G.SetClip(new RectangleF(0, bodyTop - 2, w, bottom - bodyTop + 4));
            Font f = BodyFont(p);
            float indent = Store.Lib.Settings.Indent ? f.Size * 2f : 0f;
            float ls = Store.Lib.Settings.LetterSpacing;

            for (int i = 0; i < Layout.Lines.Count; i++)
            {
                LineInfo ln = Layout.Lines[i];
                float y = bodyTop + ln.Y - ScrollY;
                if (y > bottom + 4) break;
                if (y + ln.H < bodyTop - 4) continue;
                if (ln.Start < 0 || ln.Start >= ChapterBody.Length) continue;
                int take = Math.Min(ln.Len, ChapterBody.Length - ln.Start);
                if (take <= 0) continue;
                string txt = ChapterBody.Substring(ln.Start, take);
                if (txt.Length == 0) continue;
                float x = left + (ln.ParaStart ? indent : 0);

                int selA = Math.Min(SelStart, SelEnd), selB = Math.Max(SelStart, SelEnd);
                if (selA >= 0 && selB > selA)
                {
                    int a = Math.Max(ln.Start, selA);
                    int b = Math.Min(ln.Start + ln.Len, selB);
                    if (b > a)
                    {
                        float x1 = x + PrefixWidth(p, f, txt, a - ln.Start, ls);
                        float x2 = x + PrefixWidth(p, f, txt, b - ln.Start, ls);
                        p.Fill(Color.FromArgb(64, Th.Accent), new RectangleF(x1, y + 1, Math.Max(2, x2 - x1), ln.H - 4));
                    }
                }

                if (TtsStart >= 0 && TtsEnd > TtsStart)
                {
                    int a = Math.Max(ln.Start, TtsStart);
                    int b = Math.Min(ln.Start + ln.Len, TtsEnd);
                    if (b > a)
                    {
                        float x1 = x + PrefixWidth(p, f, txt, a - ln.Start, ls);
                        float x2 = x + PrefixWidth(p, f, txt, b - ln.Start, ls);
                        p.Fill(Color.FromArgb(54, Th.Accent), new RectangleF(x1, y + 1, Math.Max(2, x2 - x1), ln.H - 4));
                    }
                }

                if (Math.Abs(ls) < 0.01f) p.Text(txt, f, bodyCol, x, y);
                else
                {
                    float cx = x;
                    for (int ci = 0; ci < txt.Length; ci++)
                    {
                        p.Text(txt.Substring(ci, 1), f, bodyCol, cx, y);
                        cx += TextDoc.CharW(f, txt[ci]) + ls;
                    }
                }
            }
            p.G.Clip = oldClip;

            DrawReaderFooter(p, w, h);
            DrawTtsBar(p, w, h);
        }

        float PrefixWidth(Painter p, Font f, string txt, int n, float ls)
        {
            if (n <= 0 || txt.Length == 0) return 0;
            if (n >= txt.Length) return p.Width(txt, f) + ls * txt.Length;
            return p.Width(txt.Substring(0, n), f) + ls * n;
        }

        public RectangleF BackRect() { return new RectangleF(14, 8, 34, 28); }
        public RectangleF PrevChapterRect() { return new RectangleF(20, Hh - 34, 96, 26); }
        public RectangleF NextChapterRect() { return new RectangleF(W - 116, Hh - 34, 96, 26); }

        public RectangleF[] IconRects()
        {
            RectangleF[] r = new RectangleF[5];
            float right = W - 132 - 12;
            for (int i = 0; i < 5; i++) r[i] = new RectangleF(right - (5 - i) * 40 + 6, 10, 34, 26);
            return r;
        }

        void DrawReaderFooter(Painter p, float w, float h)
        {
            if (!Store.Lib.Settings.ShowChrome) return;
            float y = h - 44;
            p.Fill(BgCache != null ? Color.FromArgb(230, Th.Window) : Th.Window, new RectangleF(0, y, w, 44));
            p.Line(Th.Line, 0, y, w, y, 1);

            RectangleF prev = PrevChapterRect();
            RectangleF next = NextChapterRect();
            if (HoverPanel == 30) p.FillRound(Th.Hover, prev, 8);
            if (HoverPanel == 31) p.FillRound(Th.Hover, next, 8);
            p.DrawIcon(Glyph.Back, new RectangleF(prev.X + 10, prev.Y + 7, 12, 12), Th.Muted, 1.4f);
            p.TextMid("上一章", p.F(11), Th.Body, prev.X + 26, prev.Y + 13);
            p.TextMid("下一章", p.F(11), Th.Body, next.X + 12, next.Y + 13);
            p.DrawIcon(Glyph.ChevronRight, new RectangleF(next.Right - 22, next.Y + 8, 12, 12), Th.Muted, 1.4f);

            double bookPct = BookPercent(ChapterIndex, ChapterPercent());
            float cx1 = prev.Right + 30, cx2 = next.X - 30;
            string label = Store.Lib.Settings.PageMode
                ? "第 " + (PageIndex() + 1) + " / " + PageCount() + " 页"
                : (int)(ChapterPercent() * 100) + "%";
            p.TextMid(label, p.F(10.5f), Th.Muted, cx1, y + 13);
            float barX = cx1 + 86;
            string right = "全书 " + Math.Round(bookPct * 100) + "% · 已读 " + ReadChaptersCount() + "/" + Chapters.Count + " 章";
            float rw = p.Width(right, p.F(10.5f));
            p.Progress(new RectangleF(barX, y + 11, Math.Max(80, cx2 - rw - 20 - barX), 4),
                (float)bookPct, Th.Line, Th.Accent);
            p.TextMidRight(right, p.F(10.5f), Th.Faint, cx2 - 8, y + 13);
        }

        public int ReadChaptersCount()
        {
            if (Book == null || Book.ReadLog == null) return 0;
            int n = 0;
            foreach (ChapterRead x in Book.ReadLog) if (x.Percent >= 0.95f) n++;
            return n;
        }

        // ============================================================ 交互
        const float TocRowH = 52f;

        /// <summary>打开面板；目录会直接滚到当前阅读章节。</summary>
        public void SetPanel(Panel target)
        {
            OpenPanel = target;
            if (target == Panel.Toc && Chapters.Count > 0)
            {
                float contentH = Chapters.Count * TocRowH + 8;
                float viewH = Math.Max(120, Hh - 132);
                float max = Math.Max(0, contentH - viewH);
                PanelScroll = Math.Max(0, Math.Min(max, ChapterIndex * TocRowH));
            }
            else PanelScroll = 0;
            Invalidate();
        }

        bool ReaderDown(PointF m)
        {
            if (TtsBarClick(m)) return true;
            if (BackRect().Contains(m)) { GoShelf(); return true; }
            if (PrevChapterRect().Contains(m)) { GoChapter(ChapterIndex - 1); return true; }
            if (NextChapterRect().Contains(m)) { GoChapter(ChapterIndex + 1); return true; }
            RectangleF[] ic = IconRects();
            if (ic[4].Contains(m)) { OpenSettings(); return true; }
            if (ic[3].Contains(m)) { ToggleTts(); return true; }
            for (int i = 0; i < 3; i++)
            {
                if (!ic[i].Contains(m)) continue;
                Panel target = i == 0 ? Panel.Toc : i == 1 ? Panel.Bookmarks : Panel.Stats;
                if (OpenPanel == target) { OpenPanel = Panel.None; Invalidate(); }
                else SetPanel(target);
                return true;
            }
            return false;
        }

        void ReaderClick(PointF m)
        {
            if (Layout == null) return;
            int mode = Store.Lib.Settings.ClickMode;
            if (mode == 0) { NextPage(); return; }
            float third = ContentLeft() + ContentWidth() / 3f;
            float twoThirds = ContentLeft() + ContentWidth() * 2f / 3f;
            if (m.X < third) PrevPage();
            else if (m.X > twoThirds) NextPage();
            else
            {
                if (OpenPanel == Panel.Toc) { OpenPanel = Panel.None; PanelScroll = 0; Invalidate(); }
                else SetPanel(Panel.Toc);
            }
        }
    }
}
