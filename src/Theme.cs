using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Lamplight
{
    public enum ThemeKind { Paper, Night, White }

    /// <summary>调色板与度量。所有尺寸均为逻辑像素（96dpi 基准）。</summary>
    public class Theme
    {
        public string Name;
        public Color Window;
        public Color Paper;
        public Color Ink;
        public Color Body;
        public Color Muted;
        public Color Faint;
        public Color Line;
        public Color Accent;
        public Color AccentHover;
        public Color AccentSoft;
        public Color Highlight;
        public Color Hover;
        public Color Panel;
        public Color TitleBar;
        public Color Scroll;
        public bool IsDark;

        public static Theme Create(ThemeKind kind)
        {
            Theme t = new Theme();
            switch (kind)
            {
                case ThemeKind.Night:
                    t.Name = "夜读";
                    t.Window = Color.FromArgb(24, 27, 32);
                    t.Paper = Color.FromArgb(28, 32, 38);
                    t.Ink = Color.FromArgb(226, 222, 213);
                    t.Body = Color.FromArgb(206, 200, 188);
                    t.Muted = Color.FromArgb(146, 141, 130);
                    t.Faint = Color.FromArgb(108, 105, 98);
                    t.Line = Color.FromArgb(48, 53, 60);
                    t.Accent = Color.FromArgb(201, 123, 82);
                    t.AccentHover = Color.FromArgb(219, 141, 100);
                    t.AccentSoft = Color.FromArgb(52, 42, 36);
                    t.Highlight = Color.FromArgb(58, 52, 44);
                    t.Hover = Color.FromArgb(36, 41, 48);
                    t.Panel = Color.FromArgb(31, 35, 41);
                    t.TitleBar = Color.FromArgb(24, 27, 32);
                    t.Scroll = Color.FromArgb(70, 72, 76);
                    t.IsDark = true;
                    break;
                case ThemeKind.White:
                    t.Name = "极简白";
                    t.Window = Color.FromArgb(255, 255, 255);
                    t.Paper = Color.FromArgb(255, 255, 255);
                    t.Ink = Color.FromArgb(34, 34, 34);
                    t.Body = Color.FromArgb(44, 44, 44);
                    t.Muted = Color.FromArgb(128, 128, 128);
                    t.Faint = Color.FromArgb(178, 178, 178);
                    t.Line = Color.FromArgb(232, 232, 232);
                    t.Accent = Color.FromArgb(176, 82, 44);
                    t.AccentHover = Color.FromArgb(198, 100, 60);
                    t.AccentSoft = Color.FromArgb(250, 243, 238);
                    t.Highlight = Color.FromArgb(245, 245, 245);
                    t.Hover = Color.FromArgb(246, 246, 246);
                    t.Panel = Color.FromArgb(255, 255, 255);
                    t.TitleBar = Color.FromArgb(255, 255, 255);
                    t.Scroll = Color.FromArgb(200, 200, 200);
                    break;
                default:
                    t.Name = "暖纸";
                    t.Window = Color.FromArgb(246, 241, 231);
                    t.Paper = Color.FromArgb(248, 244, 236);
                    t.Ink = Color.FromArgb(43, 33, 24);
                    t.Body = Color.FromArgb(58, 48, 39);
                    t.Muted = Color.FromArgb(150, 136, 118);
                    t.Faint = Color.FromArgb(186, 173, 155);
                    t.Line = Color.FromArgb(228, 219, 204);
                    t.Accent = Color.FromArgb(176, 82, 44);
                    t.AccentHover = Color.FromArgb(198, 100, 60);
                    t.AccentSoft = Color.FromArgb(250, 242, 232);
                    t.Highlight = Color.FromArgb(240, 231, 216);
                    t.Hover = Color.FromArgb(241, 234, 222);
                    t.Panel = Color.FromArgb(252, 250, 245);
                    t.TitleBar = Color.FromArgb(246, 241, 231);
                    t.Scroll = Color.FromArgb(198, 182, 160);
                    break;
            }
            return t;
        }
    }

    /// <summary>度量常量，集中管理，便于统一改版。</summary>
    public static class M
    {
        public const float TitleBar = 44f;
        public const float RadiusPanel = 12f;
        public const float RadiusControl = 8f;
        public const float RowHeight = 62f;
        public const float Gutter = 32f;
        public const float RailWidth = 56f;
    }
}
