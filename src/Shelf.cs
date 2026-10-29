using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public float ShelfScroll;
        public int SortMode; // 0 最近阅读 1 书名 2 添加时间
        public bool ShelfBatch;
        public HashSet<string> BatchSel = new HashSet<string>();

        public void RebuildShelf()
        {
            List<BookEntry> all = new List<BookEntry>(Store.Lib.Books);
            if (!string.IsNullOrEmpty(Query))
            {
                string q = Query.Trim().ToLowerInvariant();
                List<BookEntry> f = new List<BookEntry>();
                foreach (BookEntry b in all)
                    if (b.DisplayName.ToLowerInvariant().Contains(q) || b.Path.ToLowerInvariant().Contains(q)) f.Add(b);
                all = f;
            }
            all.Sort(delegate(BookEntry a, BookEntry b)
            {
                if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;
                if (SortMode == 1) return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
                if (SortMode == 2) return b.Added.CompareTo(a.Added);
                int c = b.LastRead.CompareTo(a.LastRead);
                return c != 0 ? c : b.Added.CompareTo(a.Added);
            });
            ShelfView = all;
            if (Selected >= ShelfView.Count) Selected = ShelfView.Count - 1;
            ClampShelf();
            Invalidate();
        }

        public void ClampShelf()
        {
            float viewH = Hh - 140 - 56;
            float total = ShelfView.Count * 66 + 8;
            float max = Math.Max(0, total - viewH);
            if (ShelfScroll > max) ShelfScroll = max;
            if (ShelfScroll < 0) ShelfScroll = 0;
        }

        // ------------------------------------------------------------ 绘制
        void DrawShelf(Painter p, RectangleF area)
        {
            float w = area.Width, h = area.Height;
            p.Fill(Th.Window, new RectangleF(0, 0, w, 44));

            p.Text("书架", p.F(26, true), Th.Ink, 32, 46);
            p.Text(Store.Lib.Books.Count + " 本藏书 · " + CountReading() + " 本在读", p.F(11), Th.Muted, 34, 84);

            RectangleF batchR = BatchButtonRect();
            p.FillRound(ShelfBatch ? Th.Accent : Th.Panel, batchR, 19);
            if (!ShelfBatch) p.Stroke(Th.Line, batchR, 19, 1);
            p.TextMidCenter(ShelfBatch ? "完成" : "批量", p.F(11f, true), ShelfBatch ? Color.White : Th.Body,
                batchR.X + batchR.Width / 2, batchR.Y + 19);

            // 右侧工具
            float right = w - 28;
            float searchW = Math.Min(250, Math.Max(150, w * 0.22f));
            RectangleF addR = new RectangleF(right - 138, 52, 138, 38);
            RectangleF setR = new RectangleF(addR.X - 8 - 38, 52, 38, 38);
            RectangleF onlineR = new RectangleF(setR.X - 6 - 38, 52, 38, 38);
            RectangleF sortR = new RectangleF(onlineR.X - 10 - 128, 52, 128, 38);
            RectangleF searchR = new RectangleF(sortR.X - 8 - searchW, 52, searchW, 38);

            // 搜索框
            p.FillRound(Th.Panel, searchR, 19);
            p.Stroke(Th.Line, searchR, 19, 1);
            if (SearchFocused) p.Stroke(Th.Accent, searchR, 19, 1.4f);
            p.DrawIcon(Glyph.Search, new RectangleF(searchR.X + 14, searchR.Y + 11, 16, 16), Th.Muted, 1.4f);
            if (!SearchFocused)
            {
                if (string.IsNullOrEmpty(Query))
                    p.TextMid("搜索书名 / 文件名", p.F(11.5f), Th.Faint, searchR.X + 38, searchR.Y + 19);
                else
                    p.TextMid(p.Truncate(Query, p.F(11.5f), searchR.Width - 70), p.F(11.5f), Th.Ink, searchR.X + 38, searchR.Y + 19);
            }

            // 排序
            p.FillRound(Th.Panel, sortR, 19);
            p.Stroke(Th.Line, sortR, 19, 1);
            p.TextMid(SortName(), p.F(11.5f), Th.Body, sortR.X + 16, sortR.Y + 19);
            p.DrawIcon(Glyph.ChevronDown, new RectangleF(sortR.Right - 26, sortR.Y + 14, 12, 12), Th.Muted, 1.3f);

            // 添加
            p.FillRound(Th.Accent, addR, 19);
            p.DrawIcon(Glyph.Plus, new RectangleF(addR.X + 18, addR.Y + 11, 15, 15), Color.White, 1.8f);
            p.TextMid("添加书籍", p.F(11.5f, true), Color.White, addR.X + 42, addR.Y + 19);

            // 在线书源 / 设置
            p.FillRound(Th.Panel, onlineR, 19); p.Stroke(Th.Line, onlineR, 19, 1);
            p.DrawIcon(Glyph.Books, new RectangleF(onlineR.X + 11, onlineR.Y + 11, 16, 16), Th.Muted, 1.4f);
            p.FillRound(Th.Panel, setR, 19); p.Stroke(Th.Line, setR, 19, 1);
            p.DrawIcon(Glyph.Gear, new RectangleF(setR.X + 11, setR.Y + 11, 16, 16), Th.Muted, 1.4f);

            // 列头
            if (ShelfView.Count > 0)
            {
                p.Text("书名", p.F(10), Th.Faint, 96, 116);
                p.Text("进度", p.F(10), Th.Faint, w - 372, 116);
                p.Text("最后阅读", p.F(10), Th.Faint, w - 150, 116);
            }

            if (Editing && EditBox != null && EditBox.Visible)
            {
                RectangleF er = new RectangleF(EditBox.Left / Scale - 7, EditBox.Top / Scale - 5,
                    EditBox.Width / Scale + 14, EditBox.Height / Scale + 10);
                p.FillRound(Th.Panel, er, 7);
                p.Stroke(Th.Accent, er, 7, 1.4f);
                p.Text("Enter 保存 · Esc 取消", p.F(9), Th.Faint, er.Right + 10, er.Y + 6);
            }

            if (ShelfView.Count == 0)
            {
                DrawEmptyShelf(p, area);
                DrawShelfFooter(p, w, h);
                return;
            }

            float top = 136;
            float bottom = h - 56;
            for (int i = 0; i < ShelfView.Count; i++)
            {
                float y = top + i * 66 - ShelfScroll;
                if (y + 62 < top - 4 || y > bottom + 4) continue;
                DrawShelfRow(p, ShelfView[i], i, new RectangleF(24, y, w - 48, 62));
            }

            DrawShelfFooter(p, w, h);
        }

        string SortName()
        {
            if (SortMode == 1) return "按书名";
            if (SortMode == 2) return "按添加时间";
            return "上次阅读优先";
        }

        int CountReading()
        {
            int n = 0;
            foreach (BookEntry b in Store.Lib.Books)
                if (b.Progress != null && b.Progress.Percent > 0.001 && b.Progress.Percent < 0.999) n++;
            return n;
        }

        /// <summary>本地文件缺失才提示；在线书没有“文件”概念。</summary>
        public static bool ShelfMissing(BookEntry b)
        {
            return b != null && !b.Online && !File.Exists(b.Path);
        }

        void DrawShelfRow(Painter p, BookEntry b, int i, RectangleF r)
        {
            bool hover = i == Hover;
            bool sel = i == Selected;
            bool online = b.Online;
            bool missing = ShelfMissing(b);
            bool latest = i == 0 && b.LastRead > 0;

            if (sel) p.FillRound(Th.Highlight, r, 8);
            else if (hover) p.FillRound(Th.Hover, r, 8);

            if (ShelfBatch && BatchSel.Contains(b.Id)) p.FillRound(Th.AccentSoft, r, 8);

            // 封面块
            RectangleF cv = new RectangleF(r.X + 16, r.Y + 8, 40, 46);
            Color c1 = ColorFromId(b.Id, 0), c2 = ColorFromId(b.Id, 1);
            if (missing) { c1 = Color.FromArgb(198, 192, 182); c2 = Color.FromArgb(168, 162, 152); }
            using (System.Drawing.Drawing2D.LinearGradientBrush gb =
                new System.Drawing.Drawing2D.LinearGradientBrush(cv, c1, c2, 90f))
            {
                using (System.Drawing.Drawing2D.GraphicsPath path = Painter.Round(cv, 7))
                    p.G.FillPath(gb, path);
            }
            string initial = string.IsNullOrEmpty(b.DisplayName) ? "书" : b.DisplayName.Substring(0, 1);
            p.TextMidCenter(initial, p.F(15, false, true), Color.FromArgb(missing ? 170 : 240, 255, 255, 255),
                cv.X + cv.Width / 2, cv.Y + cv.Height / 2);

            if (ShelfBatch)
            {
                RectangleF cb = new RectangleF(r.X + 26, r.Y + 21, 20, 20);
                bool on = BatchSel.Contains(b.Id);
                p.FillRound(on ? Th.Accent : Th.Panel, cb, 5);
                p.Stroke(on ? Th.Accent : Th.Line, cb, 5, 1.2f);
                if (on) p.DrawIcon(Glyph.Check, new RectangleF(cb.X + 4, cb.Y + 4, 12, 12), Color.White, 1.6f);
            }

            // 标题 / 改名态
            Font nf = p.F(14.5f, true);
            string name = b.DisplayName;
            float nameW = Math.Min(p.Width(name, nf), r.Width - 460);
            if (i == PreviewRename)
            {
                RectangleF er = new RectangleF(r.X + 70, r.Y + 7, 300, 32);
                p.FillRound(Th.Panel, er, 7);
                p.Stroke(Th.Accent, er, 7, 1.5f);
                Font ef = p.F(13.5f);
                string etxt = "山月记 · 卷三";
                p.TextMid(etxt, ef, Th.Ink, er.X + 11, er.Y + 16);
                float tx = er.X + 12 + p.Width(etxt, ef);
                p.FillRound(Th.Accent, new RectangleF(tx, er.Y + 8, 1.4f, 16), 0.7f);
                p.TextMid("Enter 保存　Esc 取消", p.F(9), Th.Faint, er.Right + 12, er.Y + 16);
            }
            else
            {
                p.Text(Trunc(p, name, nf, nameW), nf, missing ? Th.Faint : Th.Ink, r.X + 72, r.Y + 10);
            }

            float tagX = r.X + 72 + Math.Min(p.Width(name, nf), nameW) + 10;
            if (latest && b.LastRead > 0 && i != PreviewRename)
            {
                p.Badge("上次阅读", p.F(9), Th.Accent, Th.Panel, tagX, r.Y + 20, Th.Line);
                tagX += p.Width("上次阅读", p.F(9)) + 22;
            }
            if (online && i != PreviewRename)
                p.Badge("在线", p.F(9), Th.Accent, Th.Panel, tagX, r.Y + 20, Th.Line);
            if (b.Pinned)
            {
                p.DrawIcon(Glyph.Pin, new RectangleF(r.X + 52, r.Y + 44, 13, 13), Th.Accent, 1.3f);
            }

            // 路径（改名时让位）
            bool editingRow = (i == PreviewRename) || (Editing && EditIndex == i);
            if (!editingRow)
            {
                string showPath = b.Path;
                if (!string.IsNullOrEmpty(Store.DataDir) &&
                    showPath.StartsWith(Store.DataDir, StringComparison.OrdinalIgnoreCase))
                {
                    showPath = showPath.Substring(Store.DataDir.Length).TrimStart('\\');
                }
                string sub;
                Color subColor;
                if (online)
                {
                    string host = "";
                    try { host = new Uri(b.Url ?? b.Path).Host; } catch { }
                    if (string.IsNullOrEmpty(host)) host = b.Url ?? b.Path;
                    SourceDef sd = Online.Find(b.SourceId);
                    sub = "在线 · " + host + (sd != null && !string.IsNullOrEmpty(sd.Name) ? "　·　" + sd.Name : "");
                    subColor = Th.Faint;
                }
                else if (missing)
                {
                    sub = "文件不存在 · " + showPath;
                    subColor = Color.FromArgb(178, 116, 92);
                }
                else
                {
                    sub = showPath;
                    subColor = Th.Faint;
                }
                Font sf = p.F(10.5f);
                p.TextEllipsis(sub, sf, subColor,
                    new RectangleF(r.X + 72, r.Y + 32, r.Width - 520, 18));

            }

            // 进度
            float pw = 150;
            float px = r.Right - 372;
            double pct = b.Progress != null ? b.Progress.Percent : 0;
            p.Progress(new RectangleF(px, r.Y + 28, pw, 5), (float)pct, Th.Line, Th.Accent);
            p.TextMid(pct > 0 ? (int)Math.Round(pct * 100) + "%" : "—", p.F(10.5f), Th.Muted, px + pw + 12, r.Y + 31);

            // 时间
            p.TextMidRight(Store.HumanTime(b.LastRead), p.F(10.5f), b.LastRead > 0 ? Th.Muted : Th.Faint, r.Right - 84, r.Y + 31);

            // 更多
            RectangleF more = new RectangleF(r.Right - 56, r.Y + 17, 28, 28);
            if (hover) p.FillRound(Th.Line, more, 6);
            p.DrawIcon(Glyph.More, new RectangleF(more.X + 6, more.Y + 11, 16, 6), Th.Muted, 1.4f);
        }

        static void DrawEmptyShelf(Painter p, RectangleF area)
        {
            float cx = area.Width / 2, cy = area.Height / 2 - 20;
            p.DrawIcon(Glyph.Books, new RectangleF(cx - 34, cy - 90, 68, 68), Color.FromArgb(120, p.T.Muted), 1.2f);
            p.TextCenter("书架还是空的", p.F(19, true), p.T.Ink, cx, cy);
            p.TextCenter("把 TXT 文件拖进窗口，或按 Ctrl + O 打开", p.F(12), p.T.Muted, cx, cy + 34);
            RectangleF btn = new RectangleF(cx - 68, cy + 66, 136, 38);
            p.FillRound(p.T.Accent, btn, 19);
            p.TextMidCenter("打开文件", p.F(12, true), Color.White, cx, btn.Y + 19);
        }

        void DrawShelfFooter(Painter p, float w, float h)
        {
            if (ShelfBatch)
            {
                p.Fill(Th.Panel, new RectangleF(0, h - 56, w, 56));
                p.Line(Th.Line, 0, h - 56, w, h - 56, 1);
                p.TextMid("已选 " + BatchSel.Count + " 本", p.F(12, true), Th.Ink, 32, h - 35);
                DrawBatchBtn(p, BatchAllRect(), BatchSel.Count >= ShelfView.Count && ShelfView.Count > 0 ? "取消全选" : "全选", false);
                DrawBatchBtn(p, BatchRemoveRect(), "移出书架", false);
                DrawBatchBtn(p, BatchDeleteRect(), "删除文件", true);
                return;
            }
            DrawShelfFooterNormal(p, w, h);
        }

        void DrawBatchBtn(Painter p, RectangleF r, string text, bool danger)
        {
            p.FillRound(danger ? Color.FromArgb(Th.IsDark ? 90 : 30, 196, 74, 58) : Th.Window, r, 15);
            p.Stroke(danger ? Color.FromArgb(120, 196, 74, 58) : Th.Line, r, 15, 1);
            p.TextMidCenter(text, p.F(11f, danger), danger ? Color.FromArgb(225, 120, 96) : Th.Body,
                r.X + r.Width / 2, r.Y + r.Height / 2);
        }

        void DrawShelfFooterNormal(Painter p, float w, float h)
        {
            p.Line(Th.Line, 32, h - 52, w - 32, h - 52, 1);
            p.TextMid("文件仍在原位置 · 从书架移除不会删除文件", p.F(10.5f), Th.Faint, 32, h - 30);
            p.TextMidRight("Ctrl+O 添加 · F2 重命名 · Delete 移除", p.F(10.5f), Th.Faint, w - 32, h - 30);
        }

        static readonly Color[,] CoverPalette = new Color[,]
        {
            { Color.FromArgb(198,120,86), Color.FromArgb(122,58,44) },
            { Color.FromArgb(76,106,122), Color.FromArgb(34,50,64) },
            { Color.FromArgb(160,144,96), Color.FromArgb(88,76,46) },
            { Color.FromArgb(122,98,118), Color.FromArgb(60,42,60) },
            { Color.FromArgb(86,118,100), Color.FromArgb(40,62,52) },
            { Color.FromArgb(108,116,128), Color.FromArgb(52,58,70) },
            { Color.FromArgb(176,132,96), Color.FromArgb(110,76,52) },
            { Color.FromArgb(88,120,120), Color.FromArgb(44,66,66) }
        };

        static Color ColorFromId(string id, int variant)
        {
            int h = 0;
            for (int i = 0; i < id.Length; i++) h = (h * 31 + id[i]) & 0x7fffffff;
            return CoverPalette[h % 8, variant];
        }

        // ------------------------------------------------------------ 命中
        int ShelfHit(PointF m)
        {
            if (m.Y < 136 || m.Y > Hh - 56) return -1;
            int i = (int)((m.Y - 136 + ShelfScroll) / 66);
            if (i < 0 || i >= ShelfView.Count) return -1;
            return i;
        }

        RectangleF BatchButtonRect() { return new RectangleF(150, 52, 72, 38); }
        RectangleF BatchAllRect() { return new RectangleF(W - 32 - 300, Hh - 50, 76, 30); }
        RectangleF BatchRemoveRect() { return new RectangleF(W - 32 - 216, Hh - 50, 104, 30); }
        RectangleF BatchDeleteRect() { return new RectangleF(W - 32 - 104, Hh - 50, 104, 30); }

        /// <summary>批量移出/删除；confirm=true 时弹确认框。返回处理数量。</summary>
        public int BatchRemoveSelected(bool deleteFiles, bool confirm)
        {
            if (BatchSel.Count == 0) { ShowToast("先在列表里勾选书籍"); return 0; }
            int n = BatchSel.Count;
            if (confirm)
            {
                string msg = deleteFiles
                    ? "删除选中的 " + n + " 本？\n本地文件会一起删除，不可恢复；在线书只移出书架。"
                    : "从书架移出选中的 " + n + " 本？\n不会删除本地文件。";
                if (MessageBox.Show(this, msg, deleteFiles ? "批量删除" : "批量移出",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return 0;
            }
            int removed = 0;
            for (int i = Store.Lib.Books.Count - 1; i >= 0; i--)
            {
                BookEntry b = Store.Lib.Books[i];
                if (!BatchSel.Contains(b.Id)) continue;
                if (deleteFiles && !b.Online && !string.IsNullOrEmpty(b.Path) && File.Exists(b.Path))
                {
                    try { File.Delete(b.Path); } catch { }
                }
                Store.Lib.Books.RemoveAt(i);
                removed++;
            }
            BatchSel.Clear();
            ShelfBatch = false;
            Store.Save();
            RebuildShelf();
            Invalidate();
            return removed;
        }

        void BatchSelectAll()
        {
            bool all = BatchSel.Count >= ShelfView.Count && ShelfView.Count > 0;
            BatchSel.Clear();
            if (!all) foreach (BookEntry b in ShelfView) BatchSel.Add(b.Id);
            Invalidate();
        }

        RectangleF AddButtonRect() { return new RectangleF(W - 28 - 138, 52, 138, 38); }
        RectangleF SetButtonRect() { return new RectangleF(AddButtonRect().X - 8 - 38, 52, 38, 38); }
        RectangleF OnlineButtonRect() { return new RectangleF(SetButtonRect().X - 6 - 38, 52, 38, 38); }
        RectangleF SortButtonRect() { return new RectangleF(OnlineButtonRect().X - 10 - 128, 52, 128, 38); }
        RectangleF SearchRect()
        {
            float searchW = Math.Min(250, Math.Max(150, W * 0.22f));
            return new RectangleF(SortButtonRect().X - 8 - searchW, 52, searchW, 38);
        }

        void ShelfDown(PointF m, bool right)
        {
            if (m.Y < 44 && !right) { DragWindow(); return; }
            if (AddButtonRect().Contains(m)) { OpenFileDialog(); return; }
            if (OnlineButtonRect().Contains(m)) { OpenOnlineSearch(); return; }
            if (SetButtonRect().Contains(m)) { OpenSettings(); SettingsSection = 0; return; }
            if (SortButtonRect().Contains(m)) { OpenSortMenu(m); Invalidate(); return; }
            if (SearchRect().Contains(m)) { FocusSearch(); return; }

            if (BatchButtonRect().Contains(m))
            {
                ShelfBatch = !ShelfBatch;
                if (!ShelfBatch) BatchSel.Clear();
                Invalidate();
                return;
            }
            if (ShelfBatch)
            {
                if (BatchAllRect().Contains(m)) { BatchSelectAll(); return; }
                if (BatchRemoveRect().Contains(m)) { BatchRemoveSelected(false, true); return; }
                if (BatchDeleteRect().Contains(m)) { BatchRemoveSelected(true, true); return; }
                int bi = ShelfHit(m);
                if (bi >= 0)
                {
                    BookEntry bb = ShelfView[bi];
                    if (!BatchSel.Remove(bb.Id)) BatchSel.Add(bb.Id);
                    Invalidate();
                }
                return;
            }

            int i = ShelfHit(m);
            if (i >= 0)
            {
                Selected = i;
                RectangleF row = new RectangleF(24, 136 + i * 66 - ShelfScroll, W - 48, 62);
                if (right || m.X > row.Right - 64)
                {
                    Selected = i;
                    OpenBookMenu(m);
                    Invalidate();
                }
                else
                {
                    OpenBook(ShelfView[i], true);
                }
                Invalidate();
                return;
            }

            // 空状态按钮
            if (ShelfView.Count == 0)
            {
                RectangleF btn = new RectangleF(W / 2 - 68, Hh / 2 - 20 + 66, 136, 38);
                if (btn.Contains(m)) OpenFileDialog();
            }
        }

        // ------------------------------------------------------------ 操作
        public void OpenFileDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "书籍 (*.txt;*.epub)|*.txt;*.epub|文本文件 (*.txt)|*.txt|EPUB 电子书 (*.epub)|*.epub|所有文件 (*.*)|*.*";
                d.Multiselect = true;
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (string f in d.FileNames) Store.AddOrGet(f);
                    RebuildShelf();
                    Selected = 0;
                }
            }
        }

        public void BeginRename(int index)
        {
            if (index < 0 || index >= ShelfView.Count) return;
            BookEntry b = ShelfView[index];
            float y = 136 + index * 66 - ShelfScroll;
            RectangleF row = new RectangleF(24, y, W - 48, 62);
            float x = row.X + 72, width = Math.Min(320, row.Width - 460);
            EditIndex = index;
            Editing = true;
            EditBox.Text = b.DisplayName;
            EditBox.Bounds = new Rectangle((int)(x * Scale), (int)((y + 6) * Scale),
                (int)(width * Scale), (int)(26 * Scale));
            EditBox.Font = new Font("Microsoft YaHei UI", 11.5f * Scale);
            EditBox.BackColor = Th.Panel;
            EditBox.ForeColor = Th.Ink;
            EditBox.Visible = true;
            EditBox.BringToFront();
            EditBox.Focus();
            EditBox.SelectAll();
            Invalidate();
        }

        public void EndRename(bool save)
        {
            if (!Editing) return;
            Editing = false;
            EditBox.Visible = false;
            if (save && EditIndex >= 0 && EditIndex < ShelfView.Count)
            {
                BookEntry b = ShelfView[EditIndex];
                string t = EditBox.Text.Trim();
                b.Name = t == b.AutoName ? "" : t;
                Store.Save();
                ShowToast("已重命名");
            }
            EditIndex = -1;
            RebuildShelf();
        }

        public void RemoveBook(BookEntry b)
        {
            DialogResult r = MessageBox.Show(this, "从书架移除《" + b.DisplayName + "》？\n\n文件不会被删除。",
                "移除书籍", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.OK) return;
            Store.Remove(b);
            RebuildShelf();
            ShowToast("已从书架移除");
        }

        public void TogglePin(BookEntry b)
        {
            b.Pinned = !b.Pinned;
            Store.Save();
            RebuildShelf();
            ShowToast(b.Pinned ? "已置顶" : "已取消置顶");
        }

        public void FocusSearch()
        {
            SearchBox.Visible = true;
            SearchBox.Focus();
            SearchFocused = true;
        }

        static string Trunc(Painter p, string s, Font f, float maxW)
        {
            return p.Truncate(s, f, maxW);
        }
    }
}
