using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Lamplight
{
    public enum Glyph
    {
        Book, Books, Bookmark, Toc, Gear, Plus, Search, Close, Min, Max, Restore,
        More, Back, Pin, Trash, Folder, Check, Sun, Moon, Export, Edit, ChevronDown, Feather, Chart, ChevronRight, Help, Speaker
    }

    /// <summary>轻量绘制层：圆角、文本、图标、阴影。全部使用逻辑像素。</summary>
    public class Painter
    {
        public Graphics G;
        public Theme T;
        public float S; // DPI scale, 1.0 = 100%

        readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        readonly StringFormat _left = new StringFormat();
        readonly StringFormat _right = new StringFormat();
        readonly StringFormat _center = new StringFormat();
        readonly StringFormat _ellipsis = new StringFormat();

        public static string UiFamily = "Microsoft YaHei UI";
        public static string SerifFamily = "Noto Serif SC";

        public Painter(Graphics g, Theme theme, float scale)
        {
            G = g;
            T = theme;
            S = scale;
            G.SmoothingMode = SmoothingMode.AntiAlias;
            G.PixelOffsetMode = PixelOffsetMode.HighQuality;
            G.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            G.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _left.Alignment = StringAlignment.Near;
            _left.LineAlignment = StringAlignment.Near;
            _left.FormatFlags = StringFormatFlags.NoWrap;
            _right.Alignment = StringAlignment.Far;
            _right.LineAlignment = StringAlignment.Near;
            _right.FormatFlags = StringFormatFlags.NoWrap;
            _center.Alignment = StringAlignment.Center;
            _center.LineAlignment = StringAlignment.Near;
            _center.FormatFlags = StringFormatFlags.NoWrap;
            _ellipsis.Alignment = StringAlignment.Near;
            _ellipsis.LineAlignment = StringAlignment.Near;
            _ellipsis.Trimming = StringTrimming.EllipsisCharacter;
            _ellipsis.FormatFlags = StringFormatFlags.NoWrap;
        }

        // ------------------------------------------------------------ fonts
        public Font F(float size, bool bold = false, bool serif = false)
        {
            string key = (serif ? "s" : "u") + size + (bold ? "b" : "");
            Font f;
            if (_fonts.TryGetValue(key, out f)) return f;
            string fam = serif ? SerifFamily : UiFamily;
            try
            {
                f = new Font(fam, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch
            {
                f = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            }
            _fonts[key] = f;
            return f;
        }

        public float LineH(Font f)
        {
            return f.GetHeight(G);
        }

        // ------------------------------------------------------------ primitives
        public void Clear(Color c)
        {
            G.Clear(c);
        }

        public void Fill(Color c, RectangleF r)
        {
            using (SolidBrush b = new SolidBrush(c)) G.FillRectangle(b, r);
        }

        public void FillRound(Color c, RectangleF r, float rad)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (GraphicsPath p = Round(r, rad)) G.FillPath(b, p);
        }

        public void Stroke(Color c, RectangleF r, float rad, float w = 1f)
        {
            using (Pen pen = new Pen(c, w))
            using (GraphicsPath p = Round(r, rad)) G.DrawPath(pen, p);
        }

        public void Line(Color c, float x1, float y1, float x2, float y2, float w = 1f)
        {
            using (Pen pen = new Pen(c, w)) G.DrawLine(pen, x1, y1, x2, y2);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.1f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Shadow(RectangleF r, float rad, int alpha, float dy, float blur)
        {
            // 轻量阴影：两层低透明度圆角矩形，避免引入位图模糊
            for (int i = 2; i >= 1; i--)
            {
                RectangleF rr = new RectangleF(r.X - i * blur, r.Y - i * blur + dy, r.Width + i * blur * 2, r.Height + i * blur * 2);
                Color c = Color.FromArgb(alpha / (i * 3 + 2), 0, 0, 0);
                FillRound(c, rr, rad + i * blur);
            }
        }

        // ------------------------------------------------------------ text
        public SizeF Measure(string s, Font f)
        {
            return G.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic);
        }

        public float Width(string s, Font f)
        {
            return G.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        public void Text(string s, Font f, Color c, float x, float y)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (SolidBrush b = new SolidBrush(c))
                G.DrawString(s, f, b, x, y, StringFormat.GenericTypographic);
        }

        public void TextRight(string s, Font f, Color c, float right, float y)
        {
            if (string.IsNullOrEmpty(s)) return;
            float w = Width(s, f);
            Text(s, f, c, right - w, y);
        }

        public void TextCenter(string s, Font f, Color c, float cx, float y)
        {
            if (string.IsNullOrEmpty(s)) return;
            Text(s, f, c, cx - Width(s, f) / 2f, y);
        }

        /// <summary>垂直居中：以 cy 为中线。</summary>
        public void TextMid(string s, Font f, Color c, float x, float cy)
        {
            if (string.IsNullOrEmpty(s)) return;
            float h = f.GetHeight(G);
            Text(s, f, c, x, cy - h / 2f + 1f);
        }

        public void TextMidRight(string s, Font f, Color c, float right, float cy)
        {
            if (string.IsNullOrEmpty(s)) return;
            float w = Width(s, f);
            TextMid(s, f, c, right - w, cy);
        }

        public void TextMidCenter(string s, Font f, Color c, float cx, float cy)
        {
            if (string.IsNullOrEmpty(s)) return;
            float w = Width(s, f);
            TextMid(s, f, c, cx - w / 2f, cy);
        }

        public void TextEllipsis(string s, Font f, Color c, RectangleF box)
        {
            if (string.IsNullOrEmpty(s)) return;
            using (SolidBrush b = new SolidBrush(c))
                G.DrawString(s, f, b, box, _ellipsis);
        }

        public string Truncate(string s, Font f, float maxW)
        {
            if (string.IsNullOrEmpty(s) || Width(s, f) <= maxW) return s;
            int n = s.Length;
            while (n > 0 && Width(s.Substring(0, n) + "…", f) > maxW) n--;
            return s.Substring(0, n) + "…";
        }

        // ------------------------------------------------------------ controls
        public void Progress(RectangleF r, float pct, Color track, Color fill)
        {
            float rad = r.Height / 2f;
            FillRound(track, r, rad);
            if (pct > 0f)
            {
                float w = Math.Max(r.Height, r.Width * Math.Min(1f, pct));
                FillRound(fill, new RectangleF(r.X, r.Y, w, r.Height), rad);
            }
        }

        public void Badge(string text, Font f, Color fg, Color bg, float x, float cy, Color? outline = null)
        {
            float w = Width(text, f) + 14;
            float h = 18;
            RectangleF r = new RectangleF(x, cy - h / 2f, w, h);
            FillRound(bg, r, 9);
            if (outline.HasValue) Stroke(outline.Value, r, 9, 1);
            TextMidCenter(text, f, fg, x + w / 2f, cy);
        }

        public float BadgeWidth(string text, Font f)
        {
            return Width(text, f) + 14;
        }

        // ------------------------------------------------------------ icons
        public void DrawIcon(Glyph icon, RectangleF r, Color c, float weight = 1.5f)
        {
            using (Pen pen = new Pen(c, weight))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                float cx = r.X + w / 2f, cy = r.Y + h / 2f;
                switch (icon)
                {
                    case Glyph.Book:
                        G.DrawRectangle(pen, x + w * 0.18f, y + h * 0.12f, w * 0.64f, h * 0.76f);
                        G.DrawLine(pen, cx, y + h * 0.12f, cx, y + h * 0.88f);
                        break;
                    case Glyph.Books:
                        G.DrawRectangle(pen, x + w * 0.1f, y + h * 0.2f, w * 0.28f, h * 0.62f);
                        G.DrawRectangle(pen, x + w * 0.42f, y + h * 0.14f, w * 0.28f, h * 0.68f);
                        G.DrawLine(pen, x + w * 0.76f, y + h * 0.24f, x + w * 0.9f, y + h * 0.84f);
                        break;
                    case Glyph.Bookmark:
                        G.DrawLines(pen, new PointF[] {
                            new PointF(x + w*0.24f, y + h*0.12f),
                            new PointF(x + w*0.76f, y + h*0.12f),
                            new PointF(x + w*0.76f, y + h*0.88f),
                            new PointF(cx, y + h*0.66f),
                            new PointF(x + w*0.24f, y + h*0.88f),
                            new PointF(x + w*0.24f, y + h*0.12f) });
                        break;
                    case Glyph.Toc:
                        for (int i = 0; i < 3; i++)
                        {
                            float yy = y + h * (0.2f + i * 0.28f);
                            G.DrawLine(pen, x + w * 0.14f, yy, x + w * 0.24f, yy);
                            G.DrawLine(pen, x + w * 0.36f, yy, x + w * 0.86f, yy);
                        }
                        break;
                    case Glyph.Gear:
                        float R = Math.Min(w, h) * 0.42f;
                        PointF[] hex = new PointF[6];
                        for (int i = 0; i < 6; i++)
                        {
                            double ang = Math.PI / 6 + i * Math.PI / 3;
                            hex[i] = new PointF(cx + (float)Math.Cos(ang) * R, cy + (float)Math.Sin(ang) * R);
                        }
                        G.DrawPolygon(pen, hex);
                        G.DrawEllipse(pen, cx - R * 0.34f, cy - R * 0.34f, R * 0.68f, R * 0.68f);
                        break;
                    case Glyph.Plus:
                        G.DrawLine(pen, cx, y + h * 0.2f, cx, y + h * 0.8f);
                        G.DrawLine(pen, x + w * 0.2f, cy, x + w * 0.8f, cy);
                        break;
                    case Glyph.Search:
                        G.DrawEllipse(pen, x + w * 0.16f, y + h * 0.16f, w * 0.46f, h * 0.46f);
                        G.DrawLine(pen, x + w * 0.56f, y + h * 0.56f, x + w * 0.82f, y + h * 0.82f);
                        break;
                    case Glyph.Close:
                        G.DrawLine(pen, x + w * 0.3f, y + h * 0.3f, x + w * 0.7f, y + h * 0.7f);
                        G.DrawLine(pen, x + w * 0.7f, y + h * 0.3f, x + w * 0.3f, y + h * 0.7f);
                        break;
                    case Glyph.Min:
                        G.DrawLine(pen, x + w * 0.25f, cy, x + w * 0.75f, cy);
                        break;
                    case Glyph.Max:
                        G.DrawRectangle(pen, x + w * 0.25f, y + h * 0.25f, w * 0.5f, h * 0.5f);
                        break;
                    case Glyph.Restore:
                        G.DrawRectangle(pen, x + w * 0.22f, y + h * 0.3f, w * 0.44f, h * 0.44f);
                        G.DrawLine(pen, x + w * 0.34f, y + h * 0.3f, x + w * 0.34f, y + h * 0.2f);
                        G.DrawLine(pen, x + w * 0.34f, y + h * 0.2f, x + w * 0.78f, y + h * 0.2f);
                        G.DrawLine(pen, x + w * 0.78f, y + h * 0.2f, x + w * 0.78f, y + h * 0.64f);
                        break;
                    case Glyph.More:
                        for (int i = 0; i < 3; i++)
                            G.FillEllipse(new SolidBrush(c), cx - 1.2f, y + h * (0.26f + i * 0.24f) - 1.2f, 2.6f, 2.6f);
                        break;
                    case Glyph.Back:
                        G.DrawLine(pen, x + w * 0.6f, y + h * 0.22f, x + w * 0.32f, cy);
                        G.DrawLine(pen, x + w * 0.32f, cy, x + w * 0.6f, y + h * 0.78f);
                        break;
                    case Glyph.Pin:
                        G.DrawEllipse(pen, cx - w * 0.18f, y + h * 0.14f, w * 0.36f, h * 0.36f);
                        G.DrawLine(pen, cx, y + h * 0.5f, cx, y + h * 0.88f);
                        break;
                    case Glyph.Trash:
                        G.DrawRectangle(pen, x + w * 0.28f, y + h * 0.3f, w * 0.44f, h * 0.54f);
                        G.DrawLine(pen, x + w * 0.2f, y + h * 0.24f, x + w * 0.8f, y + h * 0.24f);
                        G.DrawLine(pen, x + w * 0.42f, y + h * 0.16f, x + w * 0.58f, y + h * 0.16f);
                        break;
                    case Glyph.Folder:
                        G.DrawLines(pen, new PointF[] {
                            new PointF(x + w*0.14f, y + h*0.74f),
                            new PointF(x + w*0.14f, y + h*0.26f),
                            new PointF(x + w*0.42f, y + h*0.26f),
                            new PointF(x + w*0.5f, y + h*0.36f),
                            new PointF(x + w*0.86f, y + h*0.36f),
                            new PointF(x + w*0.86f, y + h*0.74f) });
                        G.DrawLine(pen, x + w * 0.14f, y + h * 0.74f, x + w * 0.86f, y + h * 0.74f);
                        break;
                    case Glyph.Check:
                        G.DrawLines(pen, new PointF[] {
                            new PointF(x + w*0.22f, cy),
                            new PointF(x + w*0.44f, y + h*0.7f),
                            new PointF(x + w*0.8f, y + h*0.3f) });
                        break;
                    case Glyph.Sun:
                        G.DrawEllipse(pen, cx - w * 0.16f, cy - h * 0.16f, w * 0.32f, h * 0.32f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * 2 * i / 8;
                            G.DrawLine(pen, cx + (float)Math.Cos(a) * w * 0.28f, cy + (float)Math.Sin(a) * h * 0.28f,
                                cx + (float)Math.Cos(a) * w * 0.4f, cy + (float)Math.Sin(a) * h * 0.4f);
                        }
                        break;
                    case Glyph.Moon:
                        G.DrawArc(pen, x + w * 0.24f, y + h * 0.18f, w * 0.54f, h * 0.64f, 60, 250);
                        break;
                    case Glyph.Export:
                        G.DrawLine(pen, cx, y + h * 0.16f, cx, y + h * 0.62f);
                        G.DrawLines(pen, new PointF[] {
                            new PointF(cx - w*0.16f, y + h*0.34f),
                            new PointF(cx, y + h*0.16f),
                            new PointF(cx + w*0.16f, y + h*0.34f) });
                        G.DrawLines(pen, new PointF[] {
                            new PointF(x + w*0.24f, y + h*0.58f),
                            new PointF(x + w*0.24f, y + h*0.84f),
                            new PointF(x + w*0.76f, y + h*0.84f),
                            new PointF(x + w*0.76f, y + h*0.58f) });
                        break;
                    case Glyph.Edit:
                        G.DrawLine(pen, x + w * 0.2f, y + h * 0.8f, x + w * 0.72f, y + h * 0.28f);
                        G.DrawLine(pen, x + w * 0.62f, y + h * 0.2f, x + w * 0.8f, y + h * 0.38f);
                        G.DrawLine(pen, x + w * 0.2f, y + h * 0.8f, x + w * 0.2f, y + h * 0.64f);
                        break;
                    case Glyph.ChevronDown:
                        G.DrawLine(pen, x + w * 0.28f, y + h * 0.42f, cx, y + h * 0.6f);
                        G.DrawLine(pen, cx, y + h * 0.6f, x + w * 0.72f, y + h * 0.42f);
                        break;
                    case Glyph.Chart:
                        G.DrawLine(pen, x + w * 0.16f, y + h * 0.84f, x + w * 0.84f, y + h * 0.84f);
                        G.DrawLine(pen, x + w * 0.3f, y + h * 0.84f, x + w * 0.3f, y + h * 0.56f);
                        G.DrawLine(pen, cx, y + h * 0.84f, cx, y + h * 0.36f);
                        G.DrawLine(pen, x + w * 0.7f, y + h * 0.84f, x + w * 0.7f, y + h * 0.66f);
                        break;
                    case Glyph.ChevronRight:
                        G.DrawLine(pen, x + w * 0.4f, y + h * 0.22f, x + w * 0.68f, cy);
                        G.DrawLine(pen, x + w * 0.68f, cy, x + w * 0.4f, y + h * 0.78f);
                        break;
                    case Glyph.Speaker:
                        G.DrawRectangle(pen, x + w * 0.14f, y + h * 0.38f, w * 0.18f, h * 0.24f);
                        G.DrawLines(pen, new PointF[] {
                            new PointF(x + w*0.32f, y + h*0.38f),
                            new PointF(x + w*0.52f, y + h*0.22f),
                            new PointF(x + w*0.52f, y + h*0.78f),
                            new PointF(x + w*0.32f, y + h*0.62f) });
                        G.DrawArc(pen, x + w * 0.5f, y + h * 0.32f, w * 0.28f, h * 0.36f, -70, 140);
                        G.DrawArc(pen, x + w * 0.5f, y + h * 0.2f, w * 0.46f, h * 0.6f, -60, 120);
                        break;
                    case Glyph.Help:
                        G.DrawEllipse(pen, x + w * 0.14f, y + h * 0.14f, w * 0.72f, h * 0.72f);
                        G.DrawLine(pen, cx - w * 0.1f, y + h * 0.38f, cx + w * 0.06f, y + h * 0.32f);
                        G.DrawLine(pen, cx - w * 0.1f, y + h * 0.38f, cx - w * 0.02f, y + h * 0.5f);
                        G.DrawLine(pen, cx - w * 0.02f, y + h * 0.5f, cx + w * 0.04f, y + h * 0.56f);
                        G.DrawLine(pen, cx + w * 0.04f, y + h * 0.56f, cx + w * 0.04f, y + h * 0.62f);
                        G.FillEllipse(new SolidBrush(c), cx + w * 0.02f, y + h * 0.72f, 2.4f, 2.4f);
                        break;
                    case Glyph.Feather:
                        G.DrawLine(pen, x + w * 0.24f, y + h * 0.78f, x + w * 0.74f, y + h * 0.24f);
                        G.DrawArc(pen, x + w * 0.46f, y + h * 0.2f, w * 0.36f, h * 0.36f, 200, 150);
                        break;
                }
            }
        }
    }
}
