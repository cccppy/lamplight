using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public class OnlineGroup
        {
            public string Title;
            public string Author;
            public int Score;
            public List<OnlineBook> Items = new List<OnlineBook>();
        }

        public List<OnlineGroup> OnlineGroups = new List<OnlineGroup>();
        public string OnlineStatus = "";
        public bool OnlineBusy;
        public bool OnlineSearching;
        public TextBox OnlineBox;
        public bool OnlineWanted;
        public string OnlineQuery = "";
        public string OnlineKey = "";
        public float OnlineScroll;
        public int OnlineExpanded = -1;
        int OnlineToken;
        bool OnlineBarDrag;
        float OnlineDragY0;
        float OnlineDragScroll0;
        RectangleF OnlineTrack = RectangleF.Empty;

        // 预览目录浮层
        public bool OnlineTocOpen;
        public OnlineBook OnlineTocBook;
        public List<string[]> OnlineToc = new List<string[]>();
        public bool OnlineTocBusy;
        public string OnlineTocErr = "";
        public float OnlineTocScroll;
        int OnlineTocToken;
        bool OnlineTocBarDrag;
        float OnlineTocDragY0;
        float OnlineTocDragScroll0;
        public RectangleF OnlineTocTrack = RectangleF.Empty;
        public bool OnlineTocRequested;

        public List<SourceDef> Sources { get { return Online.Sources; } }

        public void SaveSources() { Online.Save(); }

        public void OpenOnlineSearch()
        {
            CloseOnlineToc();
            Current = View.Online;
            SyncMinSize();
            OpenPanel = Panel.None;
            PlaceOnlineBox();
            OnlineWanted = true;
            ShowOnlineBoxSoon();
            Invalidate();
        }

        public void HideOnlineBox()
        {
            OnlineWanted = false;
            if (OnlineBox != null)
            {
                OnlineBox.Visible = false;
                if (OnlineBox.Focused) ActiveControl = null;
            }
        }

        void ShowOnlineBoxSoon()
        {
            if (OnlineBox == null) return;
            OnlineBox.Visible = true;
            OnlineBox.BringToFront();
            try { ActiveControl = OnlineBox; OnlineBox.Focus(); } catch { }
            try
            {
                BeginInvoke((Action)delegate
                {
                    if (Current != View.Online || OnlineBox == null) return;
                    OnlineBox.Visible = true;
                    OnlineBox.BringToFront();
                    ActiveControl = OnlineBox;
                    OnlineBox.Focus();
                    OnlineBox.Select(OnlineBox.Text.Length, 0);
                });
            }
            catch { }
        }

        // ------------------------------------------------------------ 布局
        float OnlineListTop { get { return 150f; } }
        float OnlineListBottom { get { return Hh - 24f; } }
        float OnlineRowH { get { return 62f; } }
        float OnlineItemH { get { return 38f; } }

        float OnlineContentHeight()
        {
            float sum = 0;
            for (int i = 0; i < OnlineGroups.Count; i++)
            {
                sum += OnlineRowH + 6;
                if (i == OnlineExpanded) sum += OnlineGroups[i].Items.Count * OnlineItemH;
            }
            return sum;
        }

        void ClampOnlineScroll()
        {
            float max = Math.Max(0, OnlineContentHeight() - (OnlineListBottom - OnlineListTop));
            if (OnlineScroll > max) OnlineScroll = max;
            if (OnlineScroll < 0) OnlineScroll = 0;
        }

        // ------------------------------------------------------------ 目录预览
        float OnlineTocRowH { get { return 34f; } }

        public RectangleF OnlineTocCardRect()
        {
            float w = Math.Min(640, W - 120);
            float h = Math.Min(Hh - 140, 560);
            return new RectangleF((W - w) / 2, Math.Max(64, (Hh - h) / 2), w, h);
        }

        public float OnlineTocListTop { get { return OnlineTocCardRect().Y + 66; } }
        public float OnlineTocListBottom { get { return OnlineTocCardRect().Bottom - 50; } }

        RectangleF OnlineTocCloseRect()
        {
            RectangleF c = OnlineTocCardRect();
            return new RectangleF(c.Right - 46, c.Y + 14, 30, 30);
        }

        RectangleF OnlineTocStartRect()
        {
            RectangleF c = OnlineTocCardRect();
            return new RectangleF(c.Right - 136, c.Bottom - 40, 116, 30);
        }

        public int OnlineTocRowAt(PointF m)
        {
            if (!OnlineTocOpen) return -1;
            RectangleF c = OnlineTocCardRect();
            float top = OnlineTocListTop, bottom = OnlineTocListBottom;
            if (m.X < c.X + 12 || m.X > c.Right - 26 || m.Y < top || m.Y > bottom) return -1;
            int row = (int)((m.Y - top + OnlineTocScroll) / OnlineTocRowH);
            if (row < 0 || row >= OnlineToc.Count) return -1;
            return row;
        }

        float OnlineTocContentH() { return Math.Max(0, OnlineToc.Count * OnlineTocRowH); }

        void ClampOnlineTocScroll()
        {
            float max = Math.Max(0, OnlineTocContentH() - (OnlineTocListBottom - OnlineTocListTop));
            if (OnlineTocScroll > max) OnlineTocScroll = max;
            if (OnlineTocScroll < 0) OnlineTocScroll = 0;
        }

        public bool OnlineTocBarHit(PointF m)
        {
            return OnlineTocOpen && OnlineTocTrack.Height > 0 &&
                   m.X > OnlineTocTrack.X && m.X < OnlineTocTrack.Right &&
                   m.Y > OnlineTocTrack.Y - 4 && m.Y < OnlineTocTrack.Bottom + 4;
        }

        public void OnlineTocBarBegin(PointF m)
        {
            OnlineTocBarDrag = true;
            OnlineTocDragY0 = m.Y;
            OnlineTocDragScroll0 = OnlineTocScroll;
            Invalidate();
        }

        public void OnlineTocBarMove(PointF m)
        {
            if (!OnlineTocBarDrag) return;
            float viewH = OnlineTocListBottom - OnlineTocListTop;
            float max = Math.Max(1, OnlineTocContentH() - viewH);
            float trackH = Math.Max(10, viewH - 8);
            OnlineTocScroll = OnlineTocDragScroll0 + (m.Y - OnlineTocDragY0) * max / trackH;
            ClampOnlineTocScroll();
            Invalidate();
        }

        public void OnlineTocBarEnd() { OnlineTocBarDrag = false; Invalidate(); }

        public void CloseOnlineToc()
        {
            OnlineTocOpen = false;
            OnlineTocToken++;
            OnlineTocBusy = false;
            OnlineTocErr = "";
            OnlineTocScroll = 0;
            OnlineToc.Clear();
            OnlineTocBook = null;
            OnlineTocTrack = RectangleF.Empty;
            OnlineHoverReset();
            Invalidate();
        }

        void OnlineHoverReset() { HoverPanel = -1; }

        public RectangleF OnlineTocButtonRect(int group, int item)
        {
            if (group < 0 || group >= OnlineGroups.Count) return RectangleF.Empty;
            float y = OnlineListTop - OnlineScroll;
            for (int i = 0; i < group; i++)
            {
                y += OnlineRowH + 6;
                if (i == OnlineExpanded) y += OnlineGroups[i].Items.Count * OnlineItemH;
            }
            RectangleF r = new RectangleF(40, y, W - 80, OnlineRowH);
            if (OnlineGroups[group].Items.Count > 1)
            {
                float iy = y + OnlineRowH + 6 + item * OnlineItemH;
                RectangleF ir = new RectangleF(56, iy, W - 112, OnlineItemH - 4);
                return new RectangleF(ir.Right - 118, ir.Y + (OnlineItemH - 4) / 2 - 13, 54, 26);
            }
            return new RectangleF(r.Right - 172, r.Y + 17, 60, 28);
        }

        void PreviewOnlineToc(OnlineBook ob)
        {
            OnlineTocRequested = true;
            SourceDef src = Online.Find(ob.SourceId);
            if (src == null) { ShowToast("书源不存在"); return; }
            HideOnlineBox();
            OnlineTocBook = ob;
            OnlineToc.Clear();
            OnlineTocErr = "";
            OnlineTocBusy = true;
            OnlineTocOpen = true;
            OnlineTocScroll = 0;
            OnlineTocTrack = RectangleF.Empty;
            int token = ++OnlineTocToken;
            Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                List<string[]> toc = null;
                try { toc = Online.GetToc(src, ob.Url, out err); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (token != OnlineTocToken || !OnlineTocOpen) return;
                        OnlineTocBusy = false;
                        if (toc == null || toc.Count == 0) OnlineTocErr = err ?? "空目录";
                        else OnlineToc = toc;
                        ClampOnlineTocScroll();
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        // ------------------------------------------------------------ 绘制
        void DrawOnline(Painter p, RectangleF area)
        {
            float w = area.Width, h = area.Height;
            p.Fill(Th.Window, new RectangleF(0, 0, w, 44));
            p.Line(Th.Line, 0, 44, w, 44, 1);
            if (HoverPanel == 80) p.FillRound(Th.Hover, new RectangleF(14, 8, 34, 28), 7);
            p.DrawIcon(Glyph.Back, new RectangleF(22, 14, 18, 16), Th.Muted, 1.5f);
            p.TextMid("在线书源", p.F(14, true), Th.Ink, 60, 22);
            p.TextMidRight(Online.Sources.Count + " 个书源", p.F(10.5f), Th.Faint, w - 160, 22);

            float bx = 40, by = 66, bw = Math.Min(420, w - 420);
            p.FillRound(Th.Panel, new RectangleF(bx, by, bw, 40), 20);
            p.Stroke(Th.Line, new RectangleF(bx, by, bw, 40), 20, 1);
            p.DrawIcon(Glyph.Search, new RectangleF(bx + 16, by + 12, 16, 16), Th.Muted, 1.4f);
            if (string.IsNullOrEmpty(OnlineQuery))
                p.TextMid("输入书名或作者，回车搜索", p.F(11.5f), Th.Faint, bx + 42, by + 20);

            RectangleF sbtn = new RectangleF(bx + bw + 12, by, 96, 40);
            p.FillRound(OnlineSearching ? Th.AccentHover : Th.Accent, sbtn, 20);
            p.TextMidCenter(OnlineSearching ? "停止" : "搜索", p.F(11.5f, true), Color.White, sbtn.X + 48, by + 20);

            RectangleF mbtn = new RectangleF(sbtn.Right + 12, by, 120, 40);
            p.FillRound(Th.Panel, mbtn, 20);
            p.Stroke(Th.Line, mbtn, 20, 1);
            p.TextMidCenter("管理书源", p.F(11.5f), Th.Body, mbtn.X + 60, by + 20);

            if (!string.IsNullOrEmpty(OnlineStatus))
                p.Text(OnlineStatus, p.F(11), Th.Muted, 40, 120);

            if (OnlineGroups.Count == 0)
            {
                string msg = Online.Sources.Count == 0 ? "还没有书源" : (OnlineSearching ? "正在搜索…" : "输入关键词，回车搜索");
                p.DrawIcon(Glyph.Books, new RectangleF(w / 2 - 30, h / 2 - 116, 60, 60), Color.FromArgb(120, Th.Muted), 1.3f);
                p.TextCenter(msg, p.F(16, true), Th.Ink, w / 2, h / 2 - 46);
                p.TextCenter(Online.Sources.Count == 0 ? "点上方「管理书源」导入书源文件" : "结果会按书名合并、按相关度排序", p.F(11), Th.Muted, w / 2, h / 2 - 14);
                return;
            }

            // 列表区裁剪
            Region old = p.G.Clip;
            p.G.SetClip(new RectangleF(0, OnlineListTop - 4, w, OnlineListBottom - OnlineListTop + 8));

            float y = OnlineListTop - OnlineScroll;
            for (int i = 0; i < OnlineGroups.Count; i++)
            {
                OnlineGroup g = OnlineGroups[i];
                RectangleF r = new RectangleF(40, y, w - 80, OnlineRowH);
                RectangleF singleToc = RectangleF.Empty;
                if (r.Bottom >= OnlineListTop - 8 && r.Y <= OnlineListBottom + 8)
                {
                    if (HoverPanel == 90 + i) p.FillRound(Th.Hover, r, 10);
                    DrawHighlightTitle(p, g.Title, string.IsNullOrEmpty(OnlineKey) ? OnlineQuery : OnlineKey, r.X + 18, r.Y + 12, 13.5f, 560);
                    string sub = string.IsNullOrEmpty(g.Author) ? "" : g.Author + "　·　";
                    sub += g.Items.Count > 1 ? (g.Items.Count + " 个书源，点开选择") : g.Items[0].SourceName;
                    p.Text(p.Truncate(sub, p.F(10.5f), 560), p.F(10.5f), Th.Muted, r.X + 18, r.Y + 38);
                    if (g.Items.Count > 1)
                    {
                        RectangleF badge = new RectangleF(r.Right - 118, r.Y + 19, 52, 24);
                        p.FillRound(Th.AccentSoft, badge, 12);
                        p.TextMidCenter(g.Items.Count + " 源", p.F(10), Th.Accent, badge.X + 26, badge.Y + 12);
                    }
                    else
                    {
                        RectangleF tocBtn = new RectangleF(r.Right - 172, r.Y + 17, 60, 28);
                        singleToc = tocBtn;
                        bool hotToc = HoverPanel == 4000 + i * 100;
                        p.FillRound(hotToc ? Th.AccentSoft : Th.Panel, tocBtn, 14);
                        p.Stroke(hotToc ? Th.Accent : Th.Line, tocBtn, 14, 1);
                        p.TextMidCenter("目录", p.F(10), hotToc ? Th.Accent : Th.Body, tocBtn.X + 30, tocBtn.Y + 14);
                    }
                    p.TextMidRight(i == OnlineExpanded ? "收起" : (g.Items.Count > 1 ? "展开" : "打开 →"),
                        p.F(10.5f, true), Th.Accent, r.Right - 24, r.Y + 31);
                    AddHit(new RectangleF(r.X, r.Y, r.Width, r.Height), "ogroup", i);
                    if (!singleToc.IsEmpty) AddHit(singleToc, "otoc", i * 100);
                }
                y += OnlineRowH + 6;

                if (i == OnlineExpanded)
                {
                    for (int k = 0; k < g.Items.Count; k++)
                    {
                        OnlineBook b = g.Items[k];
                        RectangleF ir = new RectangleF(56, y, w - 112, OnlineItemH - 4);
                        if (ir.Bottom >= OnlineListTop - 8 && ir.Y <= OnlineListBottom + 8)
                        {
                            if (HoverPanel == 2000 + i * 50 + k) p.FillRound(Th.Hover, ir, 8);
                            p.TextMid(b.SourceName, p.F(11), Th.Ink, ir.X + 12, ir.Y + (OnlineItemH - 4) / 2);
                            RectangleF tocBtn2 = new RectangleF(ir.Right - 118, ir.Y + (OnlineItemH - 4) / 2 - 13, 54, 26);
                            bool hotToc2 = HoverPanel == 4000 + i * 100 + k;
                            p.FillRound(hotToc2 ? Th.AccentSoft : Th.Panel, tocBtn2, 13);
                            p.Stroke(hotToc2 ? Th.Accent : Th.Line, tocBtn2, 13, 1);
                            p.TextMidCenter("目录", p.F(10), hotToc2 ? Th.Accent : Th.Body, tocBtn2.X + 27, tocBtn2.Y + 13);
                            p.TextMidRight("阅读 →", p.F(10.5f), Th.Accent, ir.Right - 16, ir.Y + (OnlineItemH - 4) / 2);
                            AddHit(ir, "oitem", i * 100 + k);
                            AddHit(tocBtn2, "otoc", i * 100 + k);
                        }
                        y += OnlineItemH;
                    }
                }
            }
            p.G.Clip = old;

            // 滚动条（可拖动）
            float contentH2 = OnlineContentHeight();
            float viewH2 = OnlineListBottom - OnlineListTop;
            OnlineTrack = RectangleF.Empty;
            if (contentH2 > viewH2 + 2)
            {
                float trackH = viewH2 - 8;
                float thumbH = Math.Max(28, trackH * viewH2 / contentH2);
                float max = Math.Max(1, contentH2 - viewH2);
                float t = Math.Min(1, OnlineScroll / max);
                OnlineTrack = new RectangleF(w - 26, OnlineListTop + 4, 14, trackH);
                p.FillRound(Color.FromArgb(70, Th.Line), OnlineTrack, 7);
                p.FillRound(OnlineBarDrag ? Th.Accent : Th.Muted,
                    new RectangleF(w - 23, OnlineListTop + 4 + (trackH - thumbH) * t, 8, thumbH), 4);
            }

            if (OnlineTocOpen) DrawOnlineToc(p, w, h);
        }

        void DrawOnlineToc(Painter p, float w, float h)
        {
            p.Fill(Color.FromArgb(Th.IsDark ? 150 : 105, 20, 14, 8), new RectangleF(0, 0, w, h));
            RectangleF c = OnlineTocCardRect();
            p.Shadow(c, 0, 44, 0, 9);
            p.FillRound(Th.Panel, c, 14);
            p.Stroke(Th.Line, c, 14, 1);

            string title = OnlineTocBook == null ? "目录预览" : OnlineTocBook.Title;
            p.Text(p.Truncate(title, p.F(16, true), c.Width - 120), p.F(16, true), Th.Ink, c.X + 24, c.Y + 18);
            string sub = OnlineTocBook == null ? "" : OnlineTocBook.SourceName;
            if (OnlineTocBusy) sub += " · 正在读取目录…";
            else if (OnlineTocErr.Length > 0) sub += " · " + OnlineTocErr;
            else if (OnlineToc.Count > 0) sub += " · 共 " + OnlineToc.Count + " 章";
            p.Text(p.Truncate(sub, p.F(10.5f), c.Width - 120), p.F(10.5f), Th.Faint, c.X + 24, c.Y + 44);

            RectangleF close = OnlineTocCloseRect();
            if (HoverPanel == 5000) p.FillRound(Th.Hover, close, 8);
            p.DrawIcon(Glyph.Close, new RectangleF(close.X + 7, close.Y + 7, 16, 16), Th.Muted, 1.4f);
            p.Line(Th.Line, c.X + 20, c.Y + 64, c.Right - 20, c.Y + 64, 1);

            float top = OnlineTocListTop, bottom = OnlineTocListBottom;
            Region old = p.G.Clip;
            p.G.SetClip(new RectangleF(c.X + 10, top, c.Width - 20, bottom - top));
            OnlineTocTrack = RectangleF.Empty;

            if (OnlineToc.Count == 0)
            {
                string msg = OnlineTocBusy ? "正在读取目录…" : (OnlineTocErr.Length > 0 ? OnlineTocErr : "没有目录");
                p.TextCenter(msg, p.F(12), Th.Muted, c.X + c.Width / 2, (top + bottom) / 2 - 8);
            }
            else
            {
                int first = Math.Max(0, (int)(OnlineTocScroll / OnlineTocRowH) - 1);
                for (int i = first; i < OnlineToc.Count; i++)
                {
                    float y = top + i * OnlineTocRowH - OnlineTocScroll;
                    if (y > bottom) break;
                    RectangleF rr = new RectangleF(c.X + 14, y, c.Width - 40, 30);
                    if (HoverPanel == 6000 + i) p.FillRound(Th.Hover, rr, 7);
                    p.TextMid((i + 1).ToString(), p.F(10), Th.Faint, c.X + 26, y + 15);
                    string ct = OnlineToc[i] != null && OnlineToc[i].Length > 0 ? OnlineToc[i][0] : "";
                    p.TextMid(p.Truncate(ct, p.F(12), c.Width - 150), p.F(12), Th.Body, c.X + 70, y + 15);
                }
            }

            float viewH = bottom - top;
            float contentH = OnlineTocContentH();
            if (contentH > viewH + 2)
            {
                float trackH = viewH - 8;
                float thumbH = Math.Max(28, trackH * viewH / contentH);
                float max = Math.Max(1, contentH - viewH);
                float t = Math.Min(1, OnlineTocScroll / max);
                RectangleF track = new RectangleF(c.Right - 22, top + 4, 12, trackH);
                OnlineTocTrack = track;
                p.FillRound(Color.FromArgb(60, Th.Line), track, 6);
                p.FillRound(OnlineTocBarDrag ? Th.Accent : Th.Muted,
                    new RectangleF(c.Right - 19, top + 4 + (trackH - thumbH) * t, 6, thumbH), 3);
            }
            p.G.Clip = old;

            p.Line(Th.Line, c.X + 20, c.Bottom - 48, c.Right - 20, c.Bottom - 48, 1);
            p.TextMid("点任意章节，直接从这一章开始读", p.F(10.5f), Th.Faint, c.X + 24, c.Bottom - 28);
            RectangleF start = OnlineTocStartRect();
            bool hotStart = HoverPanel == 5001;
            p.FillRound(hotStart ? Th.Accent : Th.AccentSoft, start, 15);
            p.TextMidCenter("从第一章开始", p.F(10.5f, true), hotStart ? Color.White : Th.Accent, start.X + start.Width / 2, start.Y + 15);
        }

        void DrawHighlightTitle(Painter p, string title, string key, float x, float y, float size, float maxW)
        {
            Font f = p.F(size, true);
            string t = title ?? "";
            string k = (key ?? "").Trim();
            if (k.Length == 0) { p.Text(p.Truncate(t, f, maxW), f, Th.Ink, x, y); return; }
            int at = t.IndexOf(k, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                p.Text(p.Truncate(t, f, maxW), f, Th.Ink, x, y);
                return;
            }
            string before = t.Substring(0, at);
            string hit = t.Substring(at, k.Length);
            string after = t.Substring(at + k.Length);
            float cx = x;
            if (before.Length > 0) { p.Text(before, f, Th.Ink, cx, y); cx += p.Width(before, f); }
            p.Text(hit, f, Th.Accent, cx, y);
            cx += p.Width(hit, f);
            if (after.Length > 0 && cx < x + maxW)
                p.Text(p.Truncate(after, f, x + maxW - cx), f, Th.Ink, cx, y);
        }

        int OnlineHit(PointF m)
        {
            for (int i = Hits.Count - 1; i >= 0; i--)
                if (((RectangleF)Hits[i][0]).Contains(m)) return i;
            return -1;
        }

        public bool OnlineBarHit(PointF m)
        {
            return OnlineTrack.Height > 0 && m.X >= W - 34 && m.Y >= OnlineListTop && m.Y <= OnlineListBottom;
        }

        public void OnlineBarBegin(PointF m)
        {
            OnlineBarDrag = true;
            OnlineDragY0 = m.Y;
            OnlineDragScroll0 = OnlineScroll;
            Invalidate();
        }

        public void OnlineBarMove(PointF m)
        {
            if (!OnlineBarDrag) return;
            float viewH = OnlineListBottom - OnlineListTop;
            float contentH = OnlineContentHeight();
            float max = Math.Max(1, contentH - viewH);
            float trackH = Math.Max(10, viewH - 8);
            OnlineScroll = OnlineDragScroll0 + (m.Y - OnlineDragY0) * max / trackH;
            ClampOnlineScroll();
            Invalidate();
        }

        public void OnlineBarEnd() { OnlineBarDrag = false; Invalidate(); }

        int OnlineHoverCode(PointF m)
        {
            if (OnlineTocOpen)
            {
                if (OnlineTocCloseRect().Contains(m)) return 5000;
                if (OnlineTocStartRect().Contains(m)) return 5001;
                if (OnlineTocBarHit(m)) return 5002;
                int row = OnlineTocRowAt(m);
                return row >= 0 ? 6000 + row : -1;
            }
            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                if (!((RectangleF)Hits[i][0]).Contains(m)) continue;
                string a = (string)Hits[i][1];
                int idx = (int)Hits[i][2];
                if (a == "otoc") return 4000 + idx;
                if (a == "ogroup") return 90 + idx;
                if (a == "oitem") return 2000 + idx;
            }
            return -1;
        }

        public bool OnlineClick(PointF m)
        {
            if (OnlineTocOpen) return OnlineTocClick(m);
            if (m.Y < 44 && m.X < 52) { GoShelf(); return true; }
            RectangleF sbtn = new RectangleF(40 + Math.Min(420, W - 420) + 12, 66, 96, 40);
            if (sbtn.Contains(m))
            {
                if (OnlineSearching) StopOnlineSearch(false);
                else StartOnlineSearch();
                return true;
            }
            RectangleF mbtn = new RectangleF(sbtn.Right + 12, 66, 120, 40);
            if (mbtn.Contains(m)) { OpenSettings(); SettingsSection = 3; Invalidate(); return true; }

            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                if (!((RectangleF)Hits[i][0]).Contains(m)) continue;
                string a = (string)Hits[i][1];
                int idx = (int)Hits[i][2];
                if (a == "otoc")
                {
                    int gi = idx / 100, ii = idx % 100;
                    if (gi < OnlineGroups.Count && ii < OnlineGroups[gi].Items.Count)
                        PreviewOnlineToc(OnlineGroups[gi].Items[ii]);
                    return true;
                }
                if (a == "ogroup")
                {
                    OnlineGroup g = OnlineGroups[idx];
                    if (g.Items.Count == 1) { OpenOnlineResult(g.Items[0]); return true; }
                    OnlineExpanded = OnlineExpanded == idx ? -1 : idx;
                    ClampOnlineScroll();
                    Invalidate();
                    return true;
                }
                if (a == "oitem")
                {
                    int gi = idx / 100, ii = idx % 100;
                    if (gi < OnlineGroups.Count && ii < OnlineGroups[gi].Items.Count) OpenOnlineResult(OnlineGroups[gi].Items[ii]);
                    return true;
                }
            }
            return false;
        }

        bool OnlineTocClick(PointF m)
        {
            RectangleF card = OnlineTocCardRect();
            if (OnlineTocCloseRect().Contains(m) || !card.Contains(m)) { CloseOnlineToc(); return true; }
            if (OnlineTocStartRect().Contains(m))
            {
                if (OnlineToc.Count > 0) OpenOnlineAt(OnlineTocBook, OnlineToc, 0);
                return true;
            }
            int row = OnlineTocRowAt(m);
            if (row >= 0) OpenOnlineAt(OnlineTocBook, OnlineToc, row);
            return true;
        }

        void OpenOnlineAt(OnlineBook ob, List<string[]> toc, int chapter)
        {
            if (ob == null || toc == null || toc.Count == 0) return;
            if (chapter < 0) chapter = 0;
            if (chapter >= toc.Count) chapter = toc.Count - 1;
            List<string[]> copy = new List<string[]>(toc);
            CloseOnlineToc();
            OpenOnlineResultNow(ob, copy, chapter);
        }

        void PlaceOnlineBox()
        {
            if (OnlineBox == null) return;
            float bw = Math.Min(420, W - 420);
            OnlineBox.Bounds = new Rectangle((int)((40 + 42) * Scale), (int)((66 + 10) * Scale),
                (int)((bw - 60) * Scale), (int)(22 * Scale));
            OnlineBox.Font = new Font("Microsoft YaHei UI", 11f * Scale);
        }

        void BuildOnlineBox()
        {
            OnlineBox = new TextBox();
            OnlineBox.BorderStyle = BorderStyle.None;
            OnlineBox.BackColor = Th.Panel;
            OnlineBox.ForeColor = Th.Ink;
            OnlineBox.Visible = false;
            OnlineBox.TextChanged += delegate { OnlineQuery = OnlineBox.Text; Invalidate(); };
            OnlineBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { StartOnlineSearch(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { OnlineBox.Visible = false; Invalidate(); }
            };
            Controls.Add(OnlineBox);
        }

        // ------------------------------------------------------------ 搜索
        void StartOnlineSearch()
        {
            if (OnlineTocOpen) CloseOnlineToc();
            string q = (OnlineBox != null ? OnlineBox.Text : OnlineQuery) ?? "";
            q = q.Trim();
            if (q.Length == 0) { OnlineStatus = "先输入关键词"; Invalidate(); return; }
            List<SourceDef> enabled = new List<SourceDef>();
            foreach (SourceDef s in Online.Sources) if (s.Enabled) enabled.Add(s);
            if (enabled.Count == 0) { OnlineStatus = "没有启用的书源，先去「管理书源」导入"; Invalidate(); return; }

            int token = ++OnlineToken;
            OnlineKey = q;
            OnlineBusy = true;
            OnlineSearching = true;
            OnlineStatus = "正在 " + enabled.Count + " 个书源中搜索…";
            OnlineGroups = new List<OnlineGroup>();
            OnlineExpanded = -1;
            OnlineScroll = 0;
            Invalidate();

            ThreadPool.QueueUserWorkItem(delegate
            {
                List<OnlineBook> all = new List<OnlineBook>();
                int done = 0, failed = 0;
                foreach (SourceDef src in enabled)
                {
                    if (token != OnlineToken) return;
                    string err;
                    List<OnlineBook> part = Online.Search(src, q, out err);
                    if (err != null) failed++;
                    else all.AddRange(part);
                    done++;
                    try
                    {
                        int d = done, f2 = failed;
                        List<OnlineBook> snapshot = new List<OnlineBook>(all);
                        BeginInvoke((Action)delegate
                        {
                            if (token != OnlineToken) return;
                            BuildGroups(snapshot, q);
                            OnlineStatus = "已搜索 " + d + "/" + enabled.Count + " 个源 · " + snapshot.Count + " 条" + (f2 > 0 ? "（" + f2 + " 个失败）" : "");
                            Invalidate();
                        });
                    }
                    catch { }
                }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (token != OnlineToken) return;
                        OnlineBusy = false;
                        OnlineSearching = false;
                        OnlineStatus = all.Count == 0
                            ? ("没有找到结果" + (failed > 0 ? "（" + failed + " 个书源失败）" : ""))
                            : ("找到 " + all.Count + " 条，合并为 " + OnlineGroups.Count + " 本" + (failed > 0 ? "（" + failed + " 个源失败）" : ""));
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        /// <summary>停止搜索：让工作线程在下一个书源前退出，已返回的结果保留。</summary>
        public void StopOnlineSearch(bool silent)
        {
            if (!OnlineSearching && !OnlineBusy) return;
            OnlineToken++;
            OnlineSearching = false;
            OnlineBusy = false;
            if (!silent)
            {
                int n = OnlineGroups.Count;
                OnlineStatus = n > 0 ? ("已停止搜索，保留 " + n + " 本结果") : "已停止搜索";
                Invalidate();
            }
        }

        public void SeedOnlineResults(bool withToc)
        {
            OnlineGroups = new List<OnlineGroup>();
            OnlineGroup g1 = new OnlineGroup { Title = "长夜将明", Author = "沈砚" };
            g1.Items.Add(new OnlineBook { Title = "长夜将明", Author = "沈砚", Url = "http://demo/1", SourceId = "demo1", SourceName = "笔趣阁" });
            g1.Items.Add(new OnlineBook { Title = "长夜将明", Author = "沈砚", Url = "http://demo/2", SourceId = "demo2", SourceName = "书趣阁" });
            OnlineGroups.Add(g1);
            OnlineGroup g2 = new OnlineGroup { Title = "山河祭", Author = "佚名" };
            g2.Items.Add(new OnlineBook { Title = "山河祭", Author = "佚名", Url = "http://demo/3", SourceId = "demo3", SourceName = "顶点小说" });
            OnlineGroups.Add(g2);
            OnlineGroup g3 = new OnlineGroup { Title = "北方的河", Author = "张承志" };
            g3.Items.Add(new OnlineBook { Title = "北方的河", Author = "张承志", Url = "http://demo/4", SourceId = "demo4", SourceName = "起点" });
            OnlineGroups.Add(g3);
            OnlineExpanded = 0;
            OnlineStatus = "找到 4 条，合并为 3 本";
            OnlineQuery = "长夜";
            OnlineKey = "长夜";
            OnlineScroll = 0;
            if (!withToc) return;
            OnlineTocBook = g1.Items[0];
            OnlineToc.Clear();
            string[] names = { "雨夜", "渡口", "旧伞", "灯下", "归途", "长街", "潮声", "远山" };
            for (int i = 0; i < 60; i++)
                OnlineToc.Add(new string[] { "第" + (i + 1) + "章 " + names[i % names.Length], "http://demo/c" + i });
            OnlineTocOpen = true;
            OnlineTocBusy = false;
            OnlineTocErr = "";
            OnlineTocScroll = 0;
        }

        public void BuildGroups(List<OnlineBook> all, string q)        {
            Dictionary<string, OnlineGroup> map = new Dictionary<string, OnlineGroup>(StringComparer.OrdinalIgnoreCase);
            List<OnlineGroup> list = new List<OnlineGroup>();
            foreach (OnlineBook b in all)
            {
                string key = NormalizeTitle(b.Title);
                if (key.Length == 0) continue;
                OnlineGroup g;
                if (!map.TryGetValue(key, out g))
                {
                    g = new OnlineGroup();
                    g.Title = (b.Title ?? "").Trim();
                    g.Author = b.Author ?? "";
                    map[key] = g;
                    list.Add(g);
                }
                g.Items.Add(b);
                if (string.IsNullOrEmpty(g.Author) && !string.IsNullOrEmpty(b.Author)) g.Author = b.Author;
            }
            foreach (OnlineGroup g in list) g.Score = Relevance(g.Title, g.Author, q) + Math.Min(10, g.Items.Count * 2);
            list.Sort(delegate(OnlineGroup a, OnlineGroup b)
            {
                if (b.Score != a.Score) return b.Score.CompareTo(a.Score);
                return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
            });
            OnlineGroups = list;
            ClampOnlineScroll();
        }

        static string NormalizeTitle(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            string s = t.Trim();
            s = s.Replace(" ", "").Replace("　", "").Replace("《", "").Replace("》", "");
            return s;
        }

        public static int Relevance(string title, string author, string q)
        {
            string t = (title ?? "").ToLowerInvariant();
            string k = (q ?? "").ToLowerInvariant();
            if (k.Length == 0) return 0;
            int s;
            if (t == k) s = 100;
            else if (t.StartsWith(k)) s = 85;
            else if (t.Contains(k)) s = 70;
            else
            {
                int hit = 0;
                foreach (char c in k) if (t.IndexOf(c) >= 0) hit++;
                s = (int)(45.0 * hit / Math.Max(1, k.Length));
            }
            if (!string.IsNullOrEmpty(author) && author.ToLowerInvariant().Contains(k)) s += 12;
            s -= Math.Min(12, Math.Abs(t.Length - k.Length) / 2);
            return s;
        }

        // ------------------------------------------------------------ 打开结果（异步取目录）
        void OpenOnlineResult(OnlineBook ob)
        {
            SourceDef src = Online.Find(ob.SourceId);
            if (src == null) { ShowToast("书源不存在"); return; }
            ShowToast("正在获取目录…");
            OnlineBusy = true;
            Invalidate();
            int token = OnlineToken;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                List<string[]> toc = null;
                try { toc = Online.GetToc(src, ob.Url, out err); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        OnlineBusy = false;
                        if (toc == null || toc.Count == 0)
                        {
                            ShowToast("目录获取失败：" + (err ?? "空目录"));
                            Invalidate();
                            return;
                        }
                        OpenOnlineResultNow(ob, toc, -1);
                    });
                }
                catch { }
            });
        }

        void OpenOnlineResultNow(OnlineBook ob, List<string[]> toc, int chapter)
        {
            try
            {
                string id = Store.MakeId("online:" + ob.Url);
                BookEntry b = Store.Find(id);
                if (b == null)
                {
                    b = new BookEntry();
                    b.Id = id;
                    b.Path = ob.Url;
                    b.AutoName = ob.Title;
                    b.Online = true;
                    b.SourceId = ob.SourceId;
                    b.Url = ob.Url;
                    b.Added = Store.Now();
                    Store.Lib.Books.Add(b);
                }
                Online.SaveToc(id, toc);
                Store.Save();
                HideOnlineBox();
                OpenBook(b, false);
                if (chapter >= 0 && chapter < Chapters.Count)
                {
                    JumpTo(chapter, 0);
                    SaveProgress();
                }
            }
            catch (Exception ex) { ShowToast("打开失败：" + ex.Message); Invalidate(); }
        }
    }
}
