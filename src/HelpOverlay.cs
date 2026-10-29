using System;
using System.Drawing;

namespace Lamplight
{
    public partial class MainForm
    {
        void DrawHelp(Painter p, float w, float h)
        {
            string[,] rows = new string[,]
            {
                { "auto", "开始 / 停止自动翻页" },
                { "nextpage", "下一页" },
                { "prevpage", "上一页" },
                { "scrollup", "向上滚动" },
                { "scrolldown", "向下滚动" },
                { "nextchapter", "下一章" },
                { "prevchapter", "上一章" },
                { "open", "打开文件" },
                { "find", "查找" },
                { "jump", "按百分比跳转" },
                { "addbookmark", "添加书签" },
                { "toc", "目录面板" },
                { "bookmarks", "书签面板" },
                { "stats", "阅读统计" },
                { "shelf", "回到书架" },
                { "settings", "打开设置" },
                { "pin", "窗口置顶" },
                { "theme", "切换主题" },
                { "help", "本帮助" },
                { "fullscreen", "最大化 / 还原" },
            };

            int n = rows.GetLength(0);
            p.Fill(Color.FromArgb(Th.IsDark ? 150 : 60, 20, 14, 8), new RectangleF(0, 0, w, h));
            float pw = Math.Min(560, w - 80);
            float ph = Math.Min(150 + n * 26, h - 60);
            float x = (w - pw) / 2, y = (h - ph) / 2;
            RectangleF box = new RectangleF(x, y, pw, ph);
            p.Shadow(box, 14, 90, 0, 6);
            p.FillRound(Th.Panel, box, 14);
            p.Stroke(Th.Line, box, 14, 1);

            p.Text("快捷键", p.F(19, true), Th.Ink, x + 28, y + 24);
            p.Text("F1 或 Esc 关闭 · 可在设置里改键", p.F(10.5f), Th.Faint, x + 92, y + 30);

            float ry = y + 74;
            for (int i = 0; i < n; i++)
            {
                if (i % 2 == 0) p.Fill(Th.Hover, new RectangleF(x + 20, ry - 3, pw - 40, 26));
                string combo = KeyMap.Get(rows[i, 0]);
                p.TextMid(string.IsNullOrEmpty(combo) ? "—" : combo, p.F(11.5f, true), Th.Ink, x + 30, ry + 10);
                p.TextMid(rows[i, 1], p.F(11.5f), Th.Muted, x + 208, ry + 10);
                ry += 26;
            }
        }
    }
}
