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

        public List<SourceDef> Sources { get { return Online.Sources; } }

        public void SaveSources() { Online.Save(); }

        public void OpenOnlineSearch()
        {
            Current = View.Online;
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
            p.FillRound(OnlineBusy ? Th.Line : Th.Accent, sbtn, 20);
            p.TextMidCenter(OnlineBusy ? "搜索中…" : "搜索", p.F(11.5f, true), OnlineBusy ? Th.Muted : Color.White, sbtn.X + 48, by + 20);

            RectangleF mbtn = new RectangleF(sbtn.Right + 12, by, 120, 40);
            p.FillRound(Th.Panel, mbtn, 20);
            p.Stroke(Th.Line, mbtn, 20, 1);
            p.TextMidCenter("管理书源", p.F(11.5f), Th.Body, mbtn.X + 60, by + 20);

            if (!string.IsNullOrEmpty(OnlineStatus))
                p.Text(OnlineStatus, p.F(11), Th.Muted, 40, 120);

            if (OnlineGroups.Count == 0)
            {
                string msg = Online.Sources.Count == 0 ? "还没有书源" : (OnlineBusy ? "正在搜索…" : "输入关键词，回车搜索");
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
                    p.TextMidRight(i == OnlineExpanded ? "收起" : (g.Items.Count > 1 ? "展开" : "打开 →"),
                        p.F(10.5f, true), Th.Accent, r.Right - 24, r.Y + 31);
                    AddHit(new RectangleF(r.X, r.Y, r.Width, r.Height), "ogroup", i);
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
                            p.TextMidRight("阅读 →", p.F(10.5f), Th.Accent, ir.Right - 16, ir.Y + (OnlineItemH - 4) / 2);
                            AddHit(ir, "oitem", i * 100 + k);
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
            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                if (!((RectangleF)Hits[i][0]).Contains(m)) continue;
                string a = (string)Hits[i][1];
                int idx = (int)Hits[i][2];
                if (a == "ogroup") return 90 + idx;
                if (a == "oitem") return 2000 + idx;
            }
            return -1;
        }

        bool OnlineClick(PointF m)
        {
            if (m.Y < 44 && m.X < 52) { GoShelf(); return true; }
            RectangleF sbtn = new RectangleF(40 + Math.Min(420, W - 420) + 12, 66, 96, 40);
            if (sbtn.Contains(m)) { StartOnlineSearch(); return true; }
            RectangleF mbtn = new RectangleF(sbtn.Right + 12, 66, 120, 40);
            if (mbtn.Contains(m)) { OpenSettings(); SettingsSection = 3; Invalidate(); return true; }

            for (int i = Hits.Count - 1; i >= 0; i--)
            {
                if (!((RectangleF)Hits[i][0]).Contains(m)) continue;
                string a = (string)Hits[i][1];
                int idx = (int)Hits[i][2];
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
            string q = (OnlineBox != null ? OnlineBox.Text : OnlineQuery) ?? "";
            q = q.Trim();
            if (q.Length == 0) { OnlineStatus = "先输入关键词"; Invalidate(); return; }
            List<SourceDef> enabled = new List<SourceDef>();
            foreach (SourceDef s in Online.Sources) if (s.Enabled) enabled.Add(s);
            if (enabled.Count == 0) { OnlineStatus = "没有启用的书源，先去「管理书源」导入"; Invalidate(); return; }

            int token = ++OnlineToken;
            OnlineKey = q;
            OnlineBusy = true;
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
                        OnlineStatus = all.Count == 0
                            ? ("没有找到结果" + (failed > 0 ? "（" + failed + " 个书源失败）" : ""))
                            : ("找到 " + all.Count + " 条，合并为 " + OnlineGroups.Count + " 本" + (failed > 0 ? "（" + failed + " 个源失败）" : ""));
                        Invalidate();
                    });
                }
                catch { }
            });
        }

        public void BuildGroups(List<OnlineBook> all, string q)
        {
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
                        OpenOnlineResultNow(ob, toc);
                    });
                }
                catch { }
            });
        }

        void OpenOnlineResultNow(OnlineBook ob, List<string[]> toc)
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
            }
            catch (Exception ex) { ShowToast("打开失败：" + ex.Message); Invalidate(); }
        }
    }
}
