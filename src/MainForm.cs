using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Lamplight
{
    public enum View { Shelf, Reader, Settings, Online }
    public enum Panel { None, Toc, Bookmarks, Stats }

    public partial class MainForm : Form
    {
        // ---- 状态
        public View Current = View.Shelf;
        public Panel OpenPanel = Panel.None;
        public Theme Th;
        public ThemeKind Kind = ThemeKind.Paper;
        public float Scale = 1f;

        public BookEntry Book;
        public string BookText = "";
        public List<Chapter> Chapters = new List<Chapter>();
        public DocLayout Layout;
        public int ChapterIndex;
        public float ScrollY;
        float WheelAccum;
        public float LastContentH;
        public string BookCharset = "";

        public int Hover = -1;
        public int Selected = -1;
        public int HoverPanel = -1;
        public int HoverBtn = -1;
        public bool DraggingScroll;
        public string CapturingAction;
        public float DragStartY;
        public float DragStartScroll;

        public string Toast = "";
        public long ToastUntil;

        public int SelStart = -1, SelEnd = -1;
        public bool Selecting;

        public TextBox EditBox;
        public bool Editing;
        public int PreviewRename = -1;
        public int EditIndex = -1;

        public List<BookEntry> ShelfView = new List<BookEntry>();
        public string Query = "";
        public TextBox SearchBox;
        public bool SearchFocused;
        public MenuMode Menu = MenuMode.None;
        public int MenuIndex = -1;

        public Bitmap BgCache;
        public Bitmap BgSoft;
        public int BgLuma = 255;
        public string BgCachePath = "";
        public System.Windows.Forms.Timer AutoTimer;
        public System.Windows.Forms.Timer TickTimer;
        public System.Windows.Forms.Timer TtsClock;
        PointF DownAt;
        bool MaybeSelect;
        long LastTickAt;

        float _lastW, _lastH;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public MainForm()
        {
            InitExtras();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(880, 560);
            ClientSize = new Size(1200, 780);
            AllowDrop = true;
            Text = "灯下";
            BackColor = Color.FromArgb(246, 241, 231);
            DoubleBuffered = true;

            Th = Theme.Create(Kind);
            AutoTimer = new System.Windows.Forms.Timer();
            AutoTimer.Interval = 40;
            AutoTimer.Tick += delegate { AutoTick(); };
            TickTimer = new System.Windows.Forms.Timer();
            TickTimer.Interval = 5000;
            TickTimer.Tick += delegate
            {
                if (Current == View.Reader && Book != null)
                {
                    Book.ReadingSeconds += 5;
                    SaveProgress();
                }
                TtsTick();
            };
            TickTimer.Start();

            TtsClock = new System.Windows.Forms.Timer();
            TtsClock.Interval = 1000;
            TtsClock.Tick += delegate
            {
                if (TtsActive && TtsDeadline != DateTime.MinValue) Invalidate();
            };
            TtsClock.Start();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            BuildEditBox();
            BuildOnlineBox();
            BuildFindBoxes();
            BuildOnlineBox();
            BuildFindBoxes();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (PreviewMode) { Scale = 1f; return; }
            using (Graphics g = CreateGraphics()) Scale = g.DpiX / 96f;
        }

        // ------------------------------------------------------------ 窗口
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int WM_GETMINMAXINFO = 0x24;
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if (m.Result.ToInt32() == 1)
                {
                    Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF, m.LParam.ToInt32() >> 16));
                    int b = (int)(6 * Scale);
                    bool left = p.X <= b, right = p.X >= ClientSize.Width - b;
                    bool top = p.Y <= b, bottom = p.Y >= ClientSize.Height - b;
                    if (top && left) { m.Result = (IntPtr)13; return; }
                    if (top && right) { m.Result = (IntPtr)14; return; }
                    if (bottom && left) { m.Result = (IntPtr)16; return; }
                    if (bottom && right) { m.Result = (IntPtr)17; return; }
                    if (left) { m.Result = (IntPtr)10; return; }
                    if (right) { m.Result = (IntPtr)11; return; }
                    if (top) { m.Result = (IntPtr)12; return; }
                    if (bottom) { m.Result = (IntPtr)15; return; }
                }
                return;
            }
            if (m.Msg == WM_GETMINMAXINFO && WindowState == FormWindowState.Maximized)
            {
                base.WndProc(ref m);
                return;
            }
            base.WndProc(ref m);
        }

        public RectangleF WindowButtonsRect
        {
            get { return new RectangleF(ClientSize.Width / Scale - 132, 0, 132, 44); }
        }

        void ToggleMax()
        {
            if (WindowState == FormWindowState.Maximized)
                WindowState = FormWindowState.Normal;
            else
                WindowState = FormWindowState.Maximized;
        }

        // ------------------------------------------------------------ 尺寸
        public float W { get { return ClientSize.Width / Scale; } }
        public float Hh { get { return ClientSize.Height / Scale; } }

        // ------------------------------------------------------------ 绘制
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintCore(e); }
            catch (Exception ex)
            {
                LogCrash("paint", ex);
                try
                {
                    Graphics g2 = e.Graphics;
                    g2.Clear(Th != null ? Th.Window : Color.White);
                    using (Font f = new Font("Microsoft YaHei UI", 12f))
                    using (SolidBrush b = new SolidBrush(Th != null ? Th.Muted : Color.Gray))
                        g2.DrawString("绘制出错：" + ex.Message, f, b, 40, 40);
                }
                catch { }
            }
        }

        public void LogCrash(string where, Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(Store.DataDir, "crash.txt");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + where + "]  " + ex + "\r\n\r\n",
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        void PaintCore(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.ScaleTransform(Scale, Scale);
            Painter p = new Painter(g, Th, Scale);
            float w = W, h = Hh;

            p.Clear(Th.Window);
            if (Current == View.Reader || Current == View.Settings) DrawBackground(p, w, h);

            if (Current == View.Shelf)
                DrawShelf(p, new RectangleF(0, 0, w, h));
            else if (Current == View.Settings)
                DrawSettings(p, new RectangleF(0, 0, w, h));
            else if (Current == View.Online)
                DrawOnline(p, new RectangleF(0, 0, w, h));
            else
                DrawReader(p, new RectangleF(0, 0, w, h));

            DrawWindowButtons(p, w);
            if (Current == View.Reader) DrawPanel(p, w, h);
            if (Menu != MenuMode.None) DrawMenu(p, w, h);
            DrawFindPanels(p, w);
            if (HelpVisible) DrawHelp(p, w, h);
            DrawToast(p, w, h);
        }

        void DrawWindowButtons(Painter p, float w)
        {
            bool maxed = WindowState == FormWindowState.Maximized;
            float bw = 44, bx = w - 132;
            for (int i = 0; i < 3; i++)
            {
                RectangleF r = new RectangleF(bx + i * bw, 0, bw, 40);
                if (HoverBtn == i) p.Fill(Th.Hover, r);
                Color c = Th.Muted;
                if (i == 2 && HoverBtn == 2) c = Color.White;
                if (i == 2 && HoverBtn == 2) p.Fill(Color.FromArgb(196, 74, 58), r);
                Glyph ic = i == 0 ? Glyph.Min : (i == 1 ? Glyph.Max : Glyph.Close);
                p.DrawIcon(ic, new RectangleF(r.X + 13, 12, 18, 18), c, 1.3f);
            }
        }

        void DrawToast(Painter p, float w, float h)
        {
            if (string.IsNullOrEmpty(Toast) || Environment.TickCount > ToastUntil) return;
            Font f = p.F(13);
            float tw = p.Width(Toast, f) + 34;
            RectangleF r = new RectangleF(w / 2 - tw / 2, h - 84, tw, 36);
            p.FillRound(Color.FromArgb(232, Th.Ink), r, 18);
            p.TextMidCenter(Toast, f, Th.Paper, w / 2, r.Y + 18);
        }

        public void ShowToast(string msg)
        {
            Toast = msg;
            ToastUntil = Environment.TickCount + 1800;
            Invalidate();
        }

        // ------------------------------------------------------------ 书
        public bool OpenBook(BookEntry b, bool restore)
        {
            try
            {
                if (b.Online)
                {
                    List<string[]> toc = Online.LoadToc(b.Id);
                    if (toc == null || toc.Count == 0) { ShowToast("目录还没下载，请重新从在线书源添加"); return false; }
                    Book = b;
                    Chapters = new List<Chapter>();
                    foreach (string[] t in toc) Chapters.Add(new Chapter { Title = t[0], Start = 0, Length = 0 });
                    Current = View.Reader;
                    OpenPanel = Panel.None;
                    HideFindBoxes();
                    ChapterIndex = restore && b.Progress != null ? Math.Min(b.Progress.Chapter, Chapters.Count - 1) : 0;
                    if (ChapterIndex < 0) ChapterIndex = 0;
                    PrepareChapter();
                    BuildLayout();
                    ScrollY = 0;
                    b.LastRead = Store.Now();
                    Store.Save();
                    ShowToast("《" + b.DisplayName + "》 · " + Chapters.Count + " 章 · 在线");
                    Invalidate();
                    return true;
                }

                byte[] data = File.ReadAllBytes(b.Path);
                string charset;
                BookText = TextDoc.Decode(data, out charset);
                BookCharset = charset;
                Chapters = TextDoc.Split(BookText, Store.Lib.Settings.ChapterRegex);
                Book = b;
                b.Added = b.Added == 0 ? Store.Now() : b.Added;
                Store.Lib.Settings.LastBook = b.Id;
                Current = View.Reader;
                OpenPanel = Panel.None;
                if (SearchBox != null) { SearchBox.Visible = false; SearchFocused = false; }
                SelStart = SelEnd = -1;

                ChapterIndex = restore && b.Progress != null ? Math.Min(b.Progress.Chapter, Chapters.Count - 1) : 0;
                if (ChapterIndex < 0) ChapterIndex = 0;
                BuildLayout();

                if (restore && b.Progress != null && b.Progress.Offset > 0)
                {
                    int li = TextDoc.LineOfOffset(Layout, b.Progress.Offset - Chapters[ChapterIndex].Start - ChapterBodySkip);
                    ScrollY = Math.Max(0, Layout.Lines[li].Y - 40);
                }
                else ScrollY = 0;

                b.LastRead = Store.Now();
                Store.Save();
                ShowToast("《" + b.DisplayName + "》 · " + Chapters.Count + " 章 · " + charset);
                Invalidate();
                return true;
            }
            catch (Exception ex)
            {
                ShowToast("打开失败：" + ex.Message);
                return false;
            }
        }

        public string ChapterText
        {
            get
            {
                if (Chapters.Count == 0) return "";
                Chapter c = Chapters[ChapterIndex];
                string t = BookText.Substring(c.Start, Math.Min(c.Length, BookText.Length - c.Start));
                int nl = t.IndexOf('\n');
                if (nl > 0 && nl < 80)
                {
                    string first = t.Substring(0, nl).Trim();
                    if (first == c.Title || first.Length < 40) t = t.Substring(nl + 1);
                }
                return t;
            }
        }

        public void BuildLayout()
        {
            if (Chapters.Count == 0) { Layout = null; return; }
            PrepareChapter();
            if (ChapterBody.Length == 0) { Layout = new DocLayout(); return; }
            using (Bitmap bmp = new Bitmap(8, 8))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Font f = p_font(g);
                float w = ContentWidth();
                float lh = Store.Lib.Settings.FontSize * Store.Lib.Settings.LineScale;
                float indent = Store.Lib.Settings.Indent ? f.Size * 2f : 0f;
                float gap = lh * Math.Max(0.1f, Store.Lib.Settings.ParaGapScale);
                Layout = TextDoc.Build(ChapterBody, w, f, lh, gap, indent, Store.Lib.Settings.LetterSpacing);
                LastContentH = Layout.Height;
            }
        }

        public Font NewBodyFont()
        {
            string fam = Store.Lib.Settings.FontFamily;
            if (string.IsNullOrEmpty(fam)) fam = Painter.SerifFamily;
            try { return new Font(fam, Store.Lib.Settings.FontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { return new Font("Microsoft YaHei UI", Store.Lib.Settings.FontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
        }

        Font p_font(Graphics g)
        {
            string fam = Store.Lib.Settings.FontFamily;
            if (string.IsNullOrEmpty(fam)) fam = Painter.SerifFamily;
            try { return new Font(fam, Store.Lib.Settings.FontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { return new Font("Microsoft YaHei UI", Store.Lib.Settings.FontSize, FontStyle.Regular, GraphicsUnit.Pixel); }
        }

        public Font BodyFont(Painter p)
        {
            return p.F(Store.Lib.Settings.FontSize, false, true);
        }

        public float ContentWidth()
        {
            float pw = Store.Lib.Settings.PageWidth;
            float margin = Math.Max(0, Math.Min(200, Store.Lib.Settings.MarginX)) * 2;
            return Math.Max(320, Math.Min(pw, W - 160 - margin));
        }

        public float ContentLeft()
        {
            return (W - ContentWidth()) / 2f;
        }

        public float HeadingH = 78f;

        public float ContentTop()
        {
            return Store.Lib.Settings.ShowChrome ? 84f : 24f;
        }

        public float BodyTop()
        {
            return ContentTop() + HeadingH;
        }

        public float ContentBottom()
        {
            return Hh - (Store.Lib.Settings.ShowChrome ? 56f : 18f);
        }

        public double BookPercent(int chapter, double withinChapter)
        {
            if (Chapters.Count == 0) return 0;
            // 在线书没有全书文本，只能用章节数估算
            if (Book != null && Book.Online)
            {
                double v = (chapter + Math.Max(0, Math.Min(1, withinChapter))) / Chapters.Count;
                return Math.Max(0, Math.Min(1, v));
            }
            if (BookText.Length == 0) return 0;
            long before = 0;
            for (int i = 0; i < chapter && i < Chapters.Count; i++) before += Chapters[i].Length;
            double inChap = (chapter < Chapters.Count ? Chapters[chapter].Length : 0) * withinChapter;
            return Math.Max(0, Math.Min(1.0, (before + inChap) / BookText.Length));
        }

        public void DragWindow()
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Current == View.Reader) SaveProgress();
            Store.Save();
            base.OnFormClosing(e);
        }

        public void GoChapter(int idx)
        {
            if (Chapters.Count == 0) return;
            idx = Math.Max(0, Math.Min(Chapters.Count - 1, idx));
            SaveProgress();
            ChapterIndex = idx;
            BuildLayout();
            ScrollY = 0;
            SelStart = SelEnd = -1;
            Invalidate();
        }

        // ------------------------------------------------------------ 时间
        void AutoTick()
        {
            if (Current != View.Reader || OpenPanel != Panel.None) return;
            ScrollY += 2.0f;
            ClampScroll();
            Invalidate();
        }

        public void ClampScroll()
        {
            if (Layout == null) return;
            float max = Math.Max(0, Layout.Height + HeadingH - (ContentBottom() - ContentTop()) + 12);
            if (Layout.Lines.Count > 0)
            {
                float pageMax = PageStart(PageCount() - 1);
                if (pageMax > max) max = pageMax;
            }
            if (ScrollY > max) ScrollY = max;
            if (ScrollY < 0) ScrollY = 0;
        }

        void ToggleAuto()
        {
            if (AutoTimer.Enabled) { AutoTimer.Stop(); ShowToast("已停止自动翻页"); }
            else { AutoTimer.Interval = 40; AutoTimer.Start(); ShowToast("自动翻页 · 空格停止"); }
        }

        // ------------------------------------------------------------ 输入
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (CapturingAction != null) return HandleKey(keyData);
            if (ActiveControl is TextBox) return base.ProcessCmdKey(ref msg, keyData);
            if (HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>按键分发（可测试，不依赖 Windows 消息）。</summary>
        public bool HandleKey(Keys keyData)
        {
            if (CapturingAction != null)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.Escape) { CapturingAction = null; Invalidate(); return true; }
                if (code == Keys.Back || code == Keys.Delete)
                {
                    KeyMap.Set(CapturingAction, "");
                    ShowToast("已清除该快捷键");
                    CapturingAction = null;
                    Invalidate();
                    return true;
                }
                if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu) return true;
                string combo = KeyMap.Format(keyData);
                if (combo == null) return true;
                string other = KeyMap.FindActionExcept(combo, CapturingAction);
                if (other != null)
                {
                    KeyMap.Set(other, "");
                    ShowToast("「" + KeyMap.Label(other) + "」的快捷键已被解除");
                }
                KeyMap.Set(CapturingAction, combo);
                CapturingAction = null;
                Invalidate();
                return true;
            }
            string key = KeyMap.Format(keyData);
            if (key != null)
            {
                string action = KeyMap.FindAction(key);
                if (action != null && RunAction(action)) return true;
            }
            return false;
        }

        void TogglePanel(Panel panel)
        {
            if (Current != View.Reader) { OpenSettings(); return; }
            if (OpenPanel == panel) { OpenPanel = Panel.None; PanelScroll = 0; Invalidate(); }
            else SetPanel(panel);
        }

        bool RunAction(string action)
        {
            if (Current == View.Reader)
            {
                switch (action)
                {
                    case "auto": ToggleAuto(); return true;
                    case "nextpage": NextPage(); return true;
                    case "prevpage": PrevPage(); return true;
                    case "pageup": PrevPage(); return true;
                    case "pagedown": NextPage(); return true;
                    case "nextchapter": GoChapter(ChapterIndex + 1); return true;
                    case "prevchapter": GoChapter(ChapterIndex - 1); return true;
                    case "scrollup": ScrollY -= 60; ClampScroll(); Invalidate(); return true;
                    case "scrolldown": ScrollY += 60; ClampScroll(); Invalidate(); return true;
                    case "home": ScrollY = 0; ClampScroll(); Invalidate(); return true;
                    case "end": ScrollY = 1e9f; ClampScroll(); Invalidate(); return true;
                    case "find": OpenFind(); return true;
                    case "jump": OpenPercentJump(); return true;
                    case "addbookmark": AddBookmark(); return true;
                    case "bookmarks": TogglePanel(Panel.Bookmarks); return true;
                    case "toc": TogglePanel(Panel.Toc); return true;
                    case "stats": TogglePanel(Panel.Stats); return true;
                    case "zoomin": SetFontSize(Store.Lib.Settings.FontSize + 1); return true;
                    case "zoomout": SetFontSize(Store.Lib.Settings.FontSize - 1); return true;
                    case "copy": CopySelection(); return true;
                    case "reload": ReloadChapter(); return true;
                }
            }
            if (Current == View.Shelf)
            {
                if (action == "scrolldown" || action == "nextpage") { Selected = Math.Min(ShelfView.Count - 1, Selected + 1); Invalidate(); return true; }
                if (action == "scrollup" || action == "prevpage") { Selected = Math.Max(0, Selected - 1); Invalidate(); return true; }
                if (action == "open") { if (Selected >= 0 && Selected < ShelfView.Count) OpenBook(ShelfView[Selected], true); else OpenFileDialog(); return true; }
                if (action == "find") { FocusSearch(); return true; }
            }
            switch (action)
            {
                case "shelf": GoShelf(); return true;
                case "settings": OpenSettings(); return true;
                case "pin": TogglePinned(); return true;
                case "theme": CycleTheme(); return true;
                case "help": HelpVisible = !HelpVisible; Invalidate(); return true;
                case "fullscreen": ToggleMax(); return true;
                case "chrome": Store.Lib.Settings.ShowChrome = !Store.Lib.Settings.ShowChrome; Store.Save(); Invalidate(); return true;
                case "hide": if (Visible) HideToTray(); else RestoreFromTray(); return true;
                case "open": OpenFileDialog(); return true;
                case "zoomin": SetFontSize(Store.Lib.Settings.FontSize + 1); return true;
                case "zoomout": SetFontSize(Store.Lib.Settings.FontSize - 1); return true;
            }
            return false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Editing) return;

            if (e.KeyCode == Keys.Escape)
            {
                if (HelpVisible) { HelpVisible = false; Invalidate(); return; }
                if (Menu != MenuMode.None) { Menu = MenuMode.None; Invalidate(); return; }
                if (OpenPanel != Panel.None) { OpenPanel = Panel.None; Invalidate(); return; }
                if (AutoTimer.Enabled) { AutoTimer.Stop(); ShowToast("已停止自动翻页"); return; }
                if (Current == View.Settings) { CloseSettings(); return; }
                if (Current == View.Online) { GoShelf(); return; }
            }
            if (e.KeyCode == Keys.F1)
            {
                HelpVisible = !HelpVisible;
                Invalidate();
                return;
            }
        }



        // ------------------------------------------------------------ 鼠标
        PointF ToLogical(MouseEventArgs e)
        {
            return new PointF(e.X / Scale, e.Y / Scale);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PointF m = ToLogical(e);
            int oldHover = Hover, oldBtn = HoverBtn, oldPanel = HoverPanel;
            Hover = -1;
            HoverBtn = -1;
            HoverPanel = -1;

            if (Current == View.Reader && TtsActive)
            {
                int ti = TtsHitIndex(m);
                if (ti != TtsHover) { TtsHover = ti; Invalidate(); }
                Cursor = ti >= 0 ? Cursors.Hand : Cursors.Default;
            }
            else if (TtsHover != -1) { TtsHover = -1; Cursor = Cursors.Default; }

            RectangleF wb = WindowButtonsRect;
            if (wb.Contains(m))
            {
                HoverBtn = (int)((m.X - wb.X) / 44);
                if (HoverBtn > 2) HoverBtn = 2;
            }

            if (Menu != MenuMode.None)
            {
                HoverPanel = MenuHit(m);
                if (HoverPanel != oldPanel) Invalidate();
                return;
            }

            if (Current == View.Settings)
            {
                if (HoverPanel != oldPanel) Invalidate();
                return;
            }
            if (OpenPanel != Panel.None && PanelBarDrag) { PanelBarMove(m); return; }
            if (Current == View.Online)
            {
                if (OnlineBarDrag) { OnlineBarMove(m); return; }
                HoverPanel = OnlineHoverCode(m);
            }
            else if (OpenPanel != Panel.None)
            {
                HoverPanel = PanelHit(m);
            }
            else if (Current == View.Shelf)
            {
                Hover = ShelfHit(m);
            }
            else if (MaybeSelect && e.Button == MouseButtons.Left)
            {
                if (Math.Abs(m.X - DownAt.X) + Math.Abs(m.Y - DownAt.Y) > 4)
                {
                    MaybeSelect = false;
                    Selecting = true;
                    SelStart = OffsetAt(DownAt);
                    SelEnd = OffsetAt(m);
                }
            }
            else if (Selecting)
            {
                UpdateSelection(m);
                Invalidate();
                return;
            }

            if (Hover != oldHover || HoverBtn != oldBtn || HoverPanel != oldPanel) Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointF m = ToLogical(e);
            if (Editing) { EndRename(true); }

            // 窗口按钮
            RectangleF wb = WindowButtonsRect;
            if (wb.Contains(m))
            {
                int idx = (int)((m.X - wb.X) / 44);
                if (idx == 0) WindowState = FormWindowState.Minimized;
                else if (idx == 1) ToggleMax();
                else Close();
                return;
            }

            if (Menu != MenuMode.None)
            {
                int hit = MenuHit(m);
                if (hit >= 0) MenuClick(hit);
                else { Menu = MenuMode.None; Invalidate(); }
                return;
            }

            if (HelpVisible) { HelpVisible = false; Invalidate(); return; }

            if (OpenPanel != Panel.None)
            {
                if (PanelBarHit(m)) { PanelBarBegin(m); return; }
                if (PanelClick(m)) return;
                if (m.X < PanelX()) { OpenPanel = Panel.None; Invalidate(); return; }
            }

            if (Current == View.Settings)
            {
                if (m.Y < 44 && m.X >= 52) { DragWindow(); return; }
                SettingsClick(m);
                return;
            }
            if (Current == View.Online)
            {
                if (m.Y < 44 && m.X >= 52) { DragWindow(); return; }
                if (OnlineBarHit(m)) { OnlineBarBegin(m); return; }
                RectangleF field = new RectangleF(40, 66, Math.Min(420, W - 420), 40);
                if (field.Contains(m))
                {
                    if (OnlineBox != null)
                    {
                        OnlineBox.Visible = true;
                        OnlineBox.BringToFront();
                        ActiveControl = OnlineBox;
                        OnlineBox.Focus();
                    }
                    return;
                }
                OnlineClick(m);
                return;
            }

            if (Current == View.Reader && TtsActive)
            {
                int ti = TtsHitIndex(m);
                if (ti >= 0) { TtsPressed = ti; Invalidate(); return; }
            }

            if (Current == View.Reader && m.Y > BodyTop() && m.X > ContentLeft() - 20 && m.X < ContentLeft() + ContentWidth() + 20
                && m.Y < ContentBottom())
            {
                DownAt = m;
                MaybeSelect = e.Button == MouseButtons.Left;
                if (e.Button == MouseButtons.Right && Store.Lib.Settings.ClickMode == 0) PrevPage();
                return;
            }

            if (Current == View.Shelf)
            {
                ShelfDown(m, e.Button == MouseButtons.Right);
                return;
            }

            if (ReaderDown(m)) return;
            if (m.Y < 44) DragWindow();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            PointF m = ToLogical(e);
            if (PanelBarDrag) { PanelBarEnd(); return; }
            if (OnlineBarDrag) { OnlineBarEnd(); return; }
            if (TtsPressed >= 0)
            {
                int pressed = TtsPressed;
                TtsPressed = -1;
                Invalidate();
                if (Current == View.Reader && TtsActive && TtsHitIndex(m) == pressed)
                {
                    bool handled = TtsBarClick(m);
                    if (handled) return;
                }
            }
            if (MaybeSelect)
            {
                MaybeSelect = false;
                if (SelStart >= 0) { SelStart = SelEnd = -1; }
                ReaderClick(m);
                return;
            }
            if (Selecting)
            {
                Selecting = false;
                if (SelStart == SelEnd) { SelStart = SelEnd = -1; }
                Invalidate();
            }
            DraggingScroll = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            PointF m = ToLogical(e);
            if (m.Y < 44 && m.X > 100 && m.X < W - 320 && !WindowButtonsRect.Contains(m)) { ToggleMax(); return; }
            if (Current == View.Shelf)
            {
                int i = ShelfHit(m);
                if (i >= 0 && i < ShelfView.Count) OpenBook(ShelfView[i], true);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Current == View.Settings)
            {
                SettingsScroll -= e.Delta / 120f * 64;
                float max = Math.Max(0, SettingsContentH - (Hh - 90));
                if (SettingsScroll > max) SettingsScroll = max;
                if (SettingsScroll < 0) SettingsScroll = 0;
                Invalidate();
                return;
            }
            if (OpenPanel == Panel.Toc || OpenPanel == Panel.Bookmarks || OpenPanel == Panel.Stats)
            {
                PanelScroll -= e.Delta / 120f * 48;
                ClampPanel();
                Invalidate();
                return;
            }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                AdjustOpacity(e.Delta > 0 ? 1 : -1);
                return;
            }
            if (Current == View.Online)
            {
                OnlineScroll -= e.Delta / 120f * 70f;
                ClampOnlineScroll();
                Invalidate();
                return;
            }
            if (Current == View.Reader && Store.Lib.Settings.PageMode && OpenPanel == Panel.None)
            {
                WheelAccum += e.Delta;
                while (WheelAccum <= -120f) { WheelAccum += 120f; NextPage(); }
                while (WheelAccum >= 120f) { WheelAccum -= 120f; PrevPage(); }
                Invalidate();
                return;
            }
            if (Current == View.Reader)
            {
                float before = ScrollY;
                ScrollY -= e.Delta / 120f * 70;
                ClampScroll();
                bool stuck = Math.Abs(ScrollY - before) < 0.5f;
                if (e.Delta < 0 && stuck && AtChapterEnd() && ChapterIndex < Chapters.Count - 1)
                {
                    SaveProgress();
                    GoChapter(ChapterIndex + 1);
                }
                else if (e.Delta > 0 && stuck && ScrollY <= 1f && ChapterIndex > 0)
                {
                    SaveProgress();
                    GoChapter(ChapterIndex - 1);
                    ScrollY = 1e9f;
                    ClampScroll();
                    TrackChapter();
                }
                Invalidate();
            }
            else
            {
                ShelfScroll -= e.Delta / 120f * 60;
                ClampShelf();
            }
        }

        // ------------------------------------------------------------ 拖放
        void OnDragEnter(object s, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        void OnDragDrop(object s, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            int n = 0;
            foreach (string f in files)
            {
                if (Directory.Exists(f))
                {
                    foreach (string sub in Directory.GetFiles(f, "*.txt")) { Store.AddOrGet(sub); n++; }
                }
                else if (f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    Store.AddOrGet(f); n++;
                }
            }
            if (n > 0)
            {
                RebuildShelf();
                Current = View.Shelf;
                ShowToast("已添加 " + n + " 本");
            }
        }

        // ------------------------------------------------------------ 杂项
        public void GoShelf()
        {
            StopTts();
            HideOnlineBox();
            if (Current == View.Reader) SaveProgress();
            Current = View.Shelf;
            OpenPanel = Panel.None;
            RebuildShelf();
            Invalidate();
        }

        public void CycleTheme()
        {
            Kind = Kind == ThemeKind.Paper ? ThemeKind.Night : (Kind == ThemeKind.Night ? ThemeKind.White : ThemeKind.Paper);
            Th = Theme.Create(Kind);
            Store.Lib.Settings.Theme = Kind.ToString();
            Store.Save();
            BackColor = Th.Window;
            Invalidate();
        }

        public void SetFontSize(float s)
        {
            s = Math.Max(14, Math.Min(34, s));
            Store.Lib.Settings.FontSize = s;
            int oldLine = Layout != null ? TextDoc.LineAtY(Layout, ScrollY) : 0;
            BuildLayout();
            if (Layout != null && Layout.Lines.Count > 0)
                ScrollY = Math.Max(0, Layout.Lines[Math.Min(oldLine, Layout.Lines.Count - 1)].Y);
            Store.Save();
            Invalidate();
        }

        public static void OpenPath(string path)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }
    }

    public enum MenuMode { None, BookRow, Sort }
}
