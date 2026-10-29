using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public float PanelScroll;
        bool PanelBarDrag;
        float PanelDragY0;
        float PanelDragScroll0;
        public float PanelContentH;
        readonly List<object[]> Hits = new List<object[]>();
        readonly List<string> MenuItems = new List<string>();
        RectangleF MenuRect;

        public float PanelX() { return W - 380; }

        void AddHit(RectangleF r, string action, int index)
        {
            Hits.Add(new object[] { r, action, index });
        }

        public bool PanelBarHit(PointF m)
        {
            return m.X > PanelX() + 356 && m.X < PanelX() + 380 && m.Y > 76 && m.Y < Hh - 20;
        }

        public void PanelBarBegin(PointF m)
        {
            PanelBarDrag = true;
            PanelDragY0 = m.Y;
            PanelDragScroll0 = PanelScroll;
            Invalidate();
        }

        public void PanelBarMove(PointF m)
        {
            if (!PanelBarDrag) return;
            float viewH = Hh - 20 - 76;
            float content = PanelContentH;
            if (content <= 0 && OpenPanel == Panel.Toc && Chapters.Count > 0) content = Chapters.Count * TocRowH + 8;
            float max = Math.Max(1, content - viewH + 8);
            float trackH = Math.Max(10, viewH - 8);
            PanelScroll = PanelDragScroll0 + (m.Y - PanelDragY0) * max / trackH;
            ClampPanel();
            Invalidate();
        }

        public void PanelBarEnd() { PanelBarDrag = false; Invalidate(); }

        void ClampPanel()
        {
            float viewH = Hh - 20 - 76;
            float content = PanelContentH;
            if (content <= 0 && OpenPanel == Panel.Toc && Chapters.Count > 0) content = Chapters.Count * TocRowH + 8;
            float max = Math.Max(0, content - viewH + 8);
            if (PanelScroll > max) PanelScroll = max;
            if (PanelScroll < 0) PanelScroll = 0;
        }

        // ------------------------------------------------------------ 面板
        void DrawPanel(Painter p, float w, float h)
        {
            Hits.Clear();
            if (OpenPanel == Panel.None || Current != View.Reader) return;

            p.Fill(Color.FromArgb(Th.IsDark ? 90 : 20, 24, 16, 8), new RectangleF(0, 0, w, h));

            float x = PanelX();
            RectangleF panel = new RectangleF(x, 0, 380, h);
            p.Shadow(panel, 0, 60, 0, 6);
            p.Fill(Th.Panel, panel);
            p.Line(Th.Line, x, 0, x, h, 1);

            string title = OpenPanel == Panel.Toc ? "目录" : OpenPanel == Panel.Bookmarks ? "书签" : "阅读统计";
            p.Text(title, p.F(18, true), Th.Ink, x + 28, 26);
            if (OpenPanel == Panel.Toc && Chapters.Count > 0)
                p.Text(ReadChaptersCount() + " / " + Chapters.Count + " 章已读", p.F(10.5f), Th.Faint, x + 28, 50);
            if (OpenPanel == Panel.Bookmarks && Book != null && Book.Bookmarks != null)
                p.Text(Book.Bookmarks.Count + " 条书签 · 点击任意一条直接跳转", p.F(10.5f), Th.Faint, x + 28, 50);

            RectangleF close = new RectangleF(x + 380 - 168, 20, 32, 32);
            if (HoverPanel == 900) p.FillRound(Th.Hover, close, 8);
            p.DrawIcon(Glyph.Close, new RectangleF(close.X + 8, close.Y + 8, 16, 16), Th.Muted, 1.4f);
            AddHit(close, "close", 0);

            p.Line(Th.Line, x + 24, 62, x + 356, 62, 1);

            float top = 76, bottom = h - 20;
            Region old = p.G.Clip;
            p.G.SetClip(new RectangleF(x, top, 380, bottom - top));

            if (OpenPanel == Panel.Toc)
                PanelContentH = DrawToc(p, x, top, bottom);
            else
            {
                float cy = top - PanelScroll;
                if (OpenPanel == Panel.Bookmarks) cy = DrawBookmarks(p, x, cy);
                else cy = DrawStats(p, x, cy);
                PanelContentH = cy + PanelScroll - top;
            }
            p.G.Clip = old;

            float viewH = bottom - top;
            if (PanelContentH > viewH + 2)
            {
                float trackH = viewH - 8;
                float thumbH = Math.Max(30, trackH * viewH / PanelContentH);
                float max = PanelContentH - viewH;
                float t = max <= 0 ? 0 : Math.Min(1, PanelScroll / max);
                p.FillRound(Th.Line, new RectangleF(x + 368, top + 4 + (trackH - thumbH) * t, 4, thumbH), 2);
            }
        }

        // ------------------------------------------------------------ 目录
        float DrawToc(Painter p, float x, float top, float bottom)
        {
            float rowH = 52f;
            int n = Chapters.Count;
            int first = Math.Max(0, (int)(PanelScroll / rowH));
            int visible = (int)((bottom - top) / rowH) + 2;
            for (int i = first; i < Math.Min(n, first + visible); i++)
            {
                float y = top + i * rowH - PanelScroll;
                RectangleF r = new RectangleF(x + 12, y, 356, rowH);
                bool cur = i == ChapterIndex;
                float rp = ReadPercent(i);
                if (cur) p.FillRound(Th.AccentSoft, r, 8);
                else if (HoverPanel == i) p.FillRound(Th.Hover, r, 8);

                float dx = r.X + 18, dy = r.Y + 26;
                if (rp >= 0.95f) p.FillRound(Th.Accent, new RectangleF(dx, dy - 5, 10, 10), 5);
                else if (rp > 0.01f) p.Stroke(Th.Accent, new RectangleF(dx, dy - 5, 10, 10), 5, 2f);
                else p.Stroke(Th.Faint, new RectangleF(dx, dy - 5, 10, 10), 5, 1.4f);

                p.TextMid(p.Truncate(Chapters[i].Title, p.F(12.5f), 232), p.F(12.5f),
                    cur ? Th.Accent : Th.Ink, x + 38, r.Y + 18);

                int bmCount = BookmarkCount(i);
                if (bmCount > 0)
                {
                    RectangleF br = new RectangleF(x + 316, r.Y + 8, 30, 16);
                    p.FillRound(cur ? Th.Accent : Th.Line, br, 8);
                    p.TextMidCenter(bmCount.ToString(), p.F(8.5f, true), cur ? Color.White : Th.Body, br.X + 15, br.Y + 8);
                }

                if (rp > 0.01f)
                {
                    float bw = 232;
                    p.Progress(new RectangleF(x + 38, r.Y + 33, bw, 3), Math.Min(1f, rp), Th.Line, Th.Accent);
                    p.TextMidRight(rp >= 0.95f ? "已读" : (int)(rp * 100) + "%", p.F(8.5f), Th.Faint, x + 38 + bw + 24, r.Y + 33);
                }
                else
                {
                    p.Text("未读", p.F(8.5f), Th.Faint, x + 38, r.Y + 29);
                }

                AddHit(r, "chapter", i);
            }
            return n * rowH + 8;
        }

        int BookmarkCount(int chapter)
        {
            if (Book == null || Book.Bookmarks == null) return 0;
            int n = 0;
            foreach (BookmarkItem b in Book.Bookmarks) if (b.Chapter == chapter) n++;
            return n;
        }

        // ------------------------------------------------------------ 书签
        float DrawBookmarks(Painter p, float x, float cy)
        {
            RectangleF add = new RectangleF(x + 24, cy, 332, 38);
            p.FillRound(Th.Accent, add, 19);
            p.TextMidCenter("＋ 添加当前位置  Ctrl+B", p.F(12, true), Color.White, add.X + 166, add.Y + 19);
            AddHit(add, "addbm", 0);
            cy += 52;

            if (Book == null || Book.Bookmarks == null || Book.Bookmarks.Count == 0)
            {
                p.TextCenter("还没有书签", p.F(13), Th.Muted, x + 190, cy + 40);
                p.TextCenter("按 Ctrl+B 记住当前位置，回来点一下就跳过去", p.F(10.5f), Th.Faint, x + 190, cy + 66);
                return cy + 110;
            }

            List<BookmarkItem> list = SortedBookmarks();
            int lastChapter = -1;
            for (int i = 0; i < list.Count; i++)
            {
                BookmarkItem b = list[i];
                if (b.Chapter != lastChapter)
                {
                    p.Text(p.Truncate(Chapters[Math.Min(b.Chapter, Chapters.Count - 1)].Title, p.F(10.5f), 280),
                        p.F(10.5f), Th.Faint, x + 26, cy + 4);
                    cy += 26;
                    lastChapter = b.Chapter;
                }
                RectangleF r = new RectangleF(x + 14, cy, 352, 72);
                bool hover = HoverPanel == 1000 + i;
                if (hover) p.FillRound(Th.Hover, r, 8);
                p.FillRound(Th.Accent, new RectangleF(r.X + 12, r.Y + 18, 3, 34), 1.5f);
                p.TextEllipsis(b.Excerpt, p.F(12), Th.Body, new RectangleF(r.X + 24, r.Y + 10, 272, 20));
                p.Text("章内 " + ChapterPercent(b) + "% · " + Store.HumanTime(b.Created), p.F(9.5f), Th.Faint, r.X + 24, r.Y + 33);
                if (hover) p.Text("点击跳转 →", p.F(9.5f), Th.Accent, r.X + 24, r.Y + 51);

                AddHit(r, "gotobm", i);
                if (hover)
                {
                    RectangleF del = new RectangleF(r.Right - 38, r.Y + 24, 26, 26);
                    if (HoverPanel == 3000 + i) p.FillRound(Th.Hover, del, 6);
                    p.DrawIcon(Glyph.Trash, new RectangleF(del.X + 5, del.Y + 5, 15, 15), Th.Muted, 1.3f);
                    AddHit(del, "delbm", i);
                }
                cy += 78;
            }
            return cy;
        }

        int ChapterPercent(BookmarkItem b)
        {
            if (Chapters.Count == 0) return 0;
            Chapter c = Chapters[Math.Min(b.Chapter, Chapters.Count - 1)];
            if (c.Length <= 0) return 0;
            int rel = b.Offset - c.Start;
            return Math.Max(0, Math.Min(100, rel * 100 / c.Length));
        }

        // ------------------------------------------------------------ 统计
        /// <summary>只有每一章的字数都已知（本地 TXT/EPUB），才能统计全书字数。</summary>
        public bool StatsCharsKnown()
        {
            if (Chapters.Count == 0) return false;
            for (int i = 0; i < Chapters.Count; i++) if (Chapters[i].Length <= 0) return false;
            return true;
        }

        /// <summary>全书进度 = 已读章节 / 总章节（在线书没有可靠字数）。</summary>
        public double StatsProgress()
        {
            if (Chapters.Count == 0) return 0;
            int read = 0;
            for (int i = 0; i < Chapters.Count; i++) if (ReadPercent(i) >= 0.95f) read++;
            return (double)read / Chapters.Count;
        }

        float DrawStats(Painter p, float x, float cy)
        {
            long totalChars = 0, readChars = 0;
            int readChapters = 0, partial = 0, knownChapters = 0;
            for (int i = 0; i < Chapters.Count; i++)
            {
                long len = Chapters[i].Length;
                if (len > 0) knownChapters++;
                totalChars += len;
                float rp = ReadPercent(i);
                if (rp >= 0.95f) { readChapters++; readChars += len; }
                else if (rp > 0.01f) { partial++; readChars += (long)(len * rp); }
            }
            bool charsKnown = StatsCharsKnown() && totalChars > 0;
            double bookPct = StatsProgress();
            long secs = Book != null ? Book.ReadingSeconds : 0;
            double minutes = secs / 60.0;
            int remainChapters = Math.Max(0, Chapters.Count - readChapters);

            RectangleF card = new RectangleF(x + 14, cy, 352, 208);
            p.FillRound(Th.Window, card, 12);
            p.Text("本书进度", p.F(10.5f), Th.Faint, card.X + 18, card.Y + 16);
            string pctLabel = bookPct > 0 && bookPct < 0.01 ? (bookPct * 100).ToString("0.0") + "%" : Math.Round(bookPct * 100) + "%";
            p.Text(pctLabel, p.F(34, true), Th.Accent, card.X + 18, card.Y + 34);
            p.Text("已读 " + readChapters + " 章 · 在读 " + partial + " 章 · 共 " + Chapters.Count + " 章",
                p.F(10.5f), Th.Muted, card.X + 18, card.Y + 84);
            p.Progress(new RectangleF(card.X + 18, card.Y + 110, 316, 6), (float)bookPct, Th.Line, Th.Accent);

            string charsLine = charsKnown
                ? "已读 " + Fmt(readChars) + " 字 / 共 " + Fmt(totalChars) + " 字"
                : "章节进度：已读 " + readChapters + " / " + Chapters.Count + " 章";
            p.Text(charsLine, p.F(10.5f), Th.Faint, card.X + 18, card.Y + 128);

            float rowY = card.Y + 154;
            StatCell(p, card.X + 18, rowY, "阅读时长", FmtTime(secs));
            bool enough = minutes > 1 && (charsKnown ? readChars >= 2000 : readChapters >= 2);
            string speed;
            if (!enough) speed = "—";
            else if (charsKnown) speed = (readChars / minutes).ToString("0") + " 字/分";
            else speed = (readChapters / minutes * 60).ToString("0") + " 章/时";
            StatCell(p, card.X + 130, rowY, "平均速度", speed);

            string remain = "—";
            if (enough && remainChapters > 0)
            {
                double perMin = charsKnown
                    ? (readChars / minutes > 0 ? (totalChars - readChars) / (readChars / minutes) : 0)
                    : (readChapters / minutes > 0 ? remainChapters / (readChapters / minutes) : 0);
                if (perMin > 0) remain = FmtTime((long)(perMin * 60));
            }
            StatCell(p, card.X + 250, rowY, "预计剩余", remain);
            cy += 224;

            p.Text("最近在读", p.F(11, true), Th.Ink, x + 26, cy);
            cy += 26;
            List<int> recent = new List<int>();
            for (int i = 0; i < Chapters.Count; i++)
            {
                float rp = ReadPercent(i);
                if (rp > 0.01f && rp < 0.95f) recent.Add(i);
                if (recent.Count >= 6) break;
            }
            if (recent.Count == 0)
            {
                p.Text("没有正在读的章节", p.F(10.5f), Th.Faint, x + 26, cy);
                cy += 26;
            }
            foreach (int ci in recent)
            {
                RectangleF r = new RectangleF(x + 14, cy, 352, 40);
                if (HoverPanel == 1200 + ci) p.FillRound(Th.Hover, r, 8);
                p.TextMid(p.Truncate(Chapters[ci].Title, p.F(11.5f), 250), p.F(11.5f), Th.Body, r.X + 14, r.Y + 13);
                p.TextMidRight((int)(ReadPercent(ci) * 100) + "%", p.F(10), Th.Accent, r.Right - 16, r.Y + 13);
                p.Progress(new RectangleF(r.X + 14, r.Y + 30, 316, 3), ReadPercent(ci), Th.Line, Th.Accent);
                AddHit(r, "chapter", ci);
                cy += 44;
            }
            return cy + 8;
        }

        void StatCell(Painter p, float x, float y, string label, string value)
        {
            p.Text(label, p.F(9.5f), Th.Faint, x, y);
            p.Text(value, p.F(12, true), Th.Ink, x, y + 16);
        }

        static string Fmt(long n)
        {
            if (n >= 10000) return (n / 10000.0).ToString("0.0") + " 万";
            return n.ToString("N0");
        }

        static string FmtTime(long secs)
        {
            if (secs < 60) return secs + " 秒";
            long m = secs / 60;
            if (m < 60) return m + " 分钟";
            return (m / 60) + " 小时 " + (m % 60) + " 分";
        }

        // ------------------------------------------------------------ 命中
        int PanelHit(PointF m)
        {
            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                if (((RectangleF)Hits[i][0]).Contains(m))
                {
                    string a = (string)Hits[i][1];
                    if (a == "gotobm") return 1000 + (int)Hits[i][2];
                    if (a == "delbm") return 1000 + (int)Hits[i][2];
                    return -2;
                }
            }
            if (m.X > PanelX()) return -2;
            return -1;
        }

        bool PanelClick(PointF m)
        {
            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                if (!((RectangleF)Hits[i][0]).Contains(m)) continue;
                string a = (string)Hits[i][1];
                int idx = (int)Hits[i][2];
                if (a == "close") { OpenPanel = Panel.None; Invalidate(); return true; }
                if (a == "chapter") { SaveProgress(); GoChapter(idx); OpenPanel = Panel.None; return true; }
                if (a == "addbm") { AddBookmark(); return true; }
                if (a == "gotobm")
                {
                    List<BookmarkItem> list = SortedBookmarks();
                    if (idx >= 0 && idx < list.Count) JumpTo(list[idx].Chapter, list[idx].Offset);
                    return true;
                }
                if (a == "delbm")
                {
                    if (MessageBox.Show(this, "删除这条书签？", "书签", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return true;
                    RemoveBookmarkAt(idx);
                    return true;
                }
            }
            return m.X > PanelX();
        }

        /// <summary>按书签面板里的顺序删除一条。</summary>
        public bool RemoveBookmarkAt(int idx)
        {
            if (Book == null || Book.Bookmarks == null) return false;
            List<BookmarkItem> list = SortedBookmarks();
            if (idx < 0 || idx >= list.Count) return false;
            Book.Bookmarks.Remove(list[idx]);
            Store.Save();
            Invalidate();
            return true;
        }

        List<BookmarkItem> SortedBookmarks()
        {
            List<BookmarkItem> list = new List<BookmarkItem>(Book.Bookmarks);
            list.Sort(delegate(BookmarkItem a, BookmarkItem b)
            {
                if (a.Chapter != b.Chapter) return a.Chapter.CompareTo(b.Chapter);
                return a.Offset.CompareTo(b.Offset);
            });
            return list;
        }

        // ------------------------------------------------------------ 菜单
        void DrawMenu(Painter p, float w, float h)
        {
            int count = MenuItems.Count;
            float width = 190, itemH = 36, pad = 6;
            float height = count * itemH + pad * 2;
            float x = Math.Min(MenuX, w - width - 12);
            float y = Math.Min(MenuY, h - height - 12);
            MenuRect = new RectangleF(x, y, width, height);
            p.Shadow(MenuRect, 10, 70, 0, 4);
            p.FillRound(Th.Panel, MenuRect, 10);
            p.Stroke(Th.Line, MenuRect, 10, 1);
            for (int i = 0; i < count; i++)
            {
                RectangleF r = new RectangleF(x + 5, y + pad + i * itemH, width - 10, itemH);
                if (HoverPanel == i) p.FillRound(Th.Hover, r, 6);
                p.TextMid(MenuItems[i], p.F(11.5f), i == count - 1 && Menu == MenuMode.BookRow ? Th.Muted : Th.Ink, r.X + 14, r.Y + 18);
            }
        }

        float MenuX, MenuY;

        void OpenBookMenu(PointF m)
        {
            Menu = MenuMode.BookRow;
            MenuX = m.X; MenuY = m.Y;
            MenuItems.Clear();
            MenuItems.Add("重命名");
            BookEntry b = Selected >= 0 && Selected < ShelfView.Count ? ShelfView[Selected] : null;
            MenuItems.Add(b != null && b.Pinned ? "取消置顶" : "置顶到最前");
            MenuItems.Add("在文件夹中显示");
            MenuItems.Add("从书架移除");
        }

        void OpenSortMenu(PointF m)
        {
            Menu = MenuMode.Sort;
            MenuX = m.X; MenuY = m.Y;
            MenuItems.Clear();
            MenuItems.Add("上次阅读优先");
            MenuItems.Add("按书名");
            MenuItems.Add("按添加时间");
        }

        int MenuHit(PointF m)
        {
            if (!MenuRect.Contains(m)) return -1;
            int i = (int)((m.Y - MenuRect.Y - 6) / 36);
            if (i < 0 || i >= MenuItems.Count) return -1;
            return i;
        }

        void MenuClick(int i)
        {
            MenuMode mode = Menu;
            Menu = MenuMode.None;
            if (mode == MenuMode.Sort)
            {
                SortMode = i;
                Store.Lib.Settings.Sort = i == 0 ? "LastRead" : (i == 1 ? "Name" : "Added");
                Store.Save();
                RebuildShelf();
                return;
            }
            if (Selected < 0 || Selected >= ShelfView.Count) return;
            BookEntry b = ShelfView[Selected];
            if (i == 0) BeginRename(Selected);
            else if (i == 1) TogglePin(b);
            else if (i == 2) OpenPath(b.Path);
            else if (i == 3) RemoveBook(b);
        }
    }
}
