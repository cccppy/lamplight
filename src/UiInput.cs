using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public bool PreviewMode;

        void BuildEditBox()
        {
            EditBox = new TextBox();
            EditBox.BorderStyle = BorderStyle.None;
            EditBox.Visible = false;
            EditBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { EndRename(true); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { EndRename(false); e.SuppressKeyPress = true; }
            };
            EditBox.LostFocus += delegate { if (Editing) EndRename(true); };
            Controls.Add(EditBox);

            SearchBox = new TextBox();
            SearchBox.BorderStyle = BorderStyle.None;
            SearchBox.Visible = false;
            SearchBox.TextChanged += delegate
            {
                Query = SearchBox.Text;
                RebuildShelf();
            };
            SearchBox.GotFocus += delegate { SearchFocused = true; Invalidate(); };
            SearchBox.LostFocus += delegate
            {
                SearchFocused = false;
                SearchBox.Visible = false;
                Invalidate();
            };
            SearchBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    SearchBox.Text = "";
                    SearchBox.Visible = false;
                    RebuildShelf();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter && ShelfView.Count > 0)
                {
                    OpenBook(ShelfView[0], true);
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(SearchBox);
        }

        public void StyleInputs()
        {
            if (EditBox != null)
            {
                EditBox.BackColor = Th.Panel;
                EditBox.ForeColor = Th.Ink;
            }
            if (SearchBox != null)
            {
                SearchBox.BackColor = Th.Panel;
                SearchBox.ForeColor = Th.Ink;
            }
        }

        void PlaceSearchBox()
        {
            if (SearchBox == null) return;
            RectangleF r = SearchRect();
            SearchBox.Bounds = new Rectangle((int)((r.X + 38) * Scale), (int)((r.Y + 8) * Scale),
                (int)((r.Width - 56) * Scale), (int)(22 * Scale));
            SearchBox.Font = new Font("Microsoft YaHei UI", 11f * Scale);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PlaceSearchBox();
            ClampShelf();
            Invalidate();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Kind = ParseTheme(Store.Lib.Settings.Theme);
            Th = Theme.Create(Kind);
            BackColor = Th.Window;
            SortMode = Store.Lib.Settings.Sort == "Name" ? 1 : (Store.Lib.Settings.Sort == "Added" ? 2 : 0);
            StyleInputs();
            ReloadBackground();
            RebuildShelf();
            PlaceSearchBox();
            ActiveControl = null;
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            Pinned = Store.Lib.Settings.TopMost;
            TopMost = Pinned;
            UiOpacity = Store.Lib.Settings.Opacity <= 0 ? 1f : Store.Lib.Settings.Opacity;
            Opacity = UiOpacity;
        }

        public static ThemeKind ParseTheme(string s)
        {
            if (s == "Night") return ThemeKind.Night;
            if (s == "White") return ThemeKind.White;
            return ThemeKind.Paper;
        }

        // ------------------------------------------------------------ 预览
        public void EnterPreview(string mode, string sampleText)
        {
            PreviewMode = true;
            Scale = 1f;
            Kind = ParseTheme(Store.Lib.Settings.Theme);
            if (mode == "night") Kind = ThemeKind.Night;
            if (mode == "white") Kind = ThemeKind.White;
            Th = Theme.Create(Kind);
            BackColor = Th.Window;

            if (sampleText != null && sampleText.Length > 0)
            {
                BookEntry b = Store.Lib.Books.Count > 0 ? Store.Lib.Books[0] : new BookEntry();
                if (Store.Lib.Books.Count == 0) { b.Id = "demo"; b.Path = "D:\\Books\\长夜将明.txt"; b.AutoName = "长夜将明"; Store.Lib.Books.Add(b); }
                Book = b;
                BookText = sampleText;
                Chapters = TextDoc.Split(BookText);
                Current = View.Reader;
                ChapterIndex = 0;
                for (int ci = 0; ci < Chapters.Count; ci++)
                    if (Chapters[ci].Length > Chapters[ChapterIndex].Length) ChapterIndex = ci;
                PrepareChapter();
                BuildLayout();
                ScrollY = 0;
            }
            else
            {
                Current = View.Shelf;
            }

            if (mode == "bookmarks") OpenPanel = Panel.Bookmarks;
            if (mode == "toc") OpenPanel = Panel.Toc;
            if (mode == "stats") OpenPanel = Panel.Stats;
            if (mode == "settings") { Current = View.Settings; SettingsSection = 0; }
            if (mode == "settings-look") { Current = View.Settings; SettingsSection = 1; }
            if (mode == "settings-keys") { Current = View.Settings; SettingsSection = 2; }
            if (mode == "settings-sources") { Current = View.Settings; SettingsSection = 3; }
            if (mode == "settings-about") { Current = View.Settings; SettingsSection = 4; }
            if (mode == "online") Current = View.Online;
            if (mode == "reader-page") { Store.Lib.Settings.PageMode = true; Current = View.Reader; }
            if (mode == "reader-typo") { Store.Lib.Settings.TextColor = "#8C2F1E"; Store.Lib.Settings.LetterSpacing = 1.5f; Store.Lib.Settings.MarginX = 60f; Store.Lib.Settings.ParaGapScale = 0.7f; Current = View.Reader; }
            if (mode == "help") HelpVisible = true;
            if (mode == "rename") { Current = View.Shelf; OpenPanel = Panel.None; }
            if (mode == "shelf") { Current = View.Shelf; OpenPanel = Panel.None; }

            if (Current == View.Reader) { BuildLayout(); ScrollY = mode == "reader-page2" ? PageStart(1) : 0; ClampScroll(); }
            RebuildShelf();
        }
    }
}
