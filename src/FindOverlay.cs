using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        TextBox FindBox;
        TextBox JumpBox;
        List<int> FindHits = new List<int>();
        int FindCursor;

        void BuildFindBoxes()
        {
            FindBox = new TextBox();
            FindBox.BorderStyle = BorderStyle.None;
            FindBox.Visible = false;
            FindBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { DoFind(FindBox.Text, true); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { FindBox.Visible = false; Invalidate(); }
            };
            FindBox.TextChanged += delegate { DoFind(FindBox.Text, false); };
            FindBox.LostFocus += HideFindOnBlur;
            Controls.Add(FindBox);

            JumpBox = new TextBox();
            JumpBox.BorderStyle = BorderStyle.None;
            JumpBox.Visible = false;
            JumpBox.LostFocus += HideFindOnBlur;
            JumpBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { DoPercentJump(JumpBox.Text); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { JumpBox.Visible = false; Invalidate(); }
            };
            Controls.Add(JumpBox);
        }

        public void OpenFind()
        {
            if (FindBox == null) return;
            FindBox.Bounds = new Rectangle((int)(((W - 380) / 2 + 64) * Scale), (int)(60 * Scale), (int)(286 * Scale), (int)(22 * Scale));
            FindBox.Font = new Font("Microsoft YaHei UI", 12f * Scale);
            FindBox.BackColor = Th.Panel;
            FindBox.ForeColor = Th.Ink;
            FindBox.Visible = true;
            FindBox.BringToFront();
            FindBox.Focus();
            FindBox.SelectAll();
        }

        public void OpenPercentJump()
        {
            if (JumpBox == null) return;
            JumpBox.Bounds = new Rectangle((int)(((W - 240) / 2 + 54) * Scale), (int)(60 * Scale), (int)(160 * Scale), (int)(22 * Scale));
            JumpBox.Font = new Font("Microsoft YaHei UI", 12f * Scale);
            JumpBox.BackColor = Th.Panel;
            JumpBox.ForeColor = Th.Ink;
            JumpBox.Visible = true;
            JumpBox.BringToFront();
            JumpBox.Focus();
            JumpBox.SelectAll();
        }

        void DoFind(string q, bool next)
        {
            if (string.IsNullOrEmpty(q) || Book == null) return;
            if (FindHits.Count == 0 || !next)
            {
                FindHits = new List<int>();
                string hay = BookText;
                int idx = 0;
                while (FindHits.Count < 500)
                {
                    int at = hay.IndexOf(q, idx, StringComparison.CurrentCultureIgnoreCase);
                    if (at < 0) break;
                    FindHits.Add(at);
                    idx = at + Math.Max(1, q.Length);
                }
                FindCursor = 0;
            }
            if (FindHits.Count == 0)
            {
                ShowToast("没有找到「" + q + "」");
                return;
            }
            if (next && FindHits.Count > 0) FindCursor = FindCursor % FindHits.Count;
            int pos = FindHits[FindCursor];
            FindCursor = (FindCursor + 1) % FindHits.Count;
            int chapter = 0;
            for (int i = 0; i < Chapters.Count; i++)
                if (Chapters[i].Start <= pos) chapter = i; else break;
            JumpTo(chapter, pos);
            SelStart = SelEnd = -1;
            ShowToast("第 " + FindCursor + " / " + FindHits.Count + " 处");
        }

        void DoPercentJump(string text)
        {
            float pct;
            if (!float.TryParse(text.Trim().Replace("%", ""), out pct)) { ShowToast("请输入 0 – 100 的数字"); return; }
            pct = Math.Max(0, Math.Min(100, pct));
            if (BookText.Length == 0) return;
            int target = (int)(BookText.Length * pct / 100f);
            int chapter = 0;
            for (int i = 0; i < Chapters.Count; i++)
                if (Chapters[i].Start <= target) chapter = i; else break;
            JumpTo(chapter, target);
            JumpBox.Visible = false;
            ShowToast("已跳到 " + (int)pct + "%");
        }

        void HideFindOnBlur(object sender, EventArgs e)
        {
            if (FindBox != null) FindBox.Visible = false;
            if (JumpBox != null) JumpBox.Visible = false;
            Invalidate();
        }

        void DrawFindPanels(Painter p, float w)
        {
            if (FindBox != null && FindBox.Visible)
            {
                RectangleF r = new RectangleF((w - 380) / 2, 48, 380, 46);
                p.Shadow(r, 12, 70, 0, 4);
                p.FillRound(Th.Panel, r, 12);
                p.Stroke(Th.Line, r, 12, 1);
                p.TextMid("查找", p.F(10.5f), Th.Faint, r.X + 18, r.Y + 23);
                p.TextMid("Enter 下一个 · Esc 关闭", p.F(9.5f), Th.Faint, r.Right - 150, r.Y + 23);
            }
            if (JumpBox != null && JumpBox.Visible)
            {
                RectangleF r = new RectangleF((w - 240) / 2, 48, 240, 46);
                p.Shadow(r, 12, 70, 0, 4);
                p.FillRound(Th.Panel, r, 12);
                p.Stroke(Th.Line, r, 12, 1);
                p.TextMid("跳到 %", p.F(10.5f), Th.Faint, r.X + 16, r.Y + 23);
            }
        }

        void HideFindBoxes()
        {
            if (FindBox != null) FindBox.Visible = false;
            if (JumpBox != null) JumpBox.Visible = false;
        }
    }
}
