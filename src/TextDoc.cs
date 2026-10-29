using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Text;

namespace Lamplight
{
    public class Chapter
    {
        public string Title;
        public int Start;
        public int Length;
    }

    public class LineInfo
    {
        public int Start;
        public int Len;
        public float Y;
        public float H;
        public bool ParaStart;
        public bool ParaEnd;
    }

    public class DocLayout
    {
        public List<LineInfo> Lines = new List<LineInfo>();
        public float Height;
        public float Width;
    }

    /// <summary>文本：编码探测、分章、断行排版。独立于窗口，便于测试。</summary>
    public static class TextDoc
    {
        static readonly Bitmap MeasureBmp = new Bitmap(4, 4);
        static readonly Graphics MeasureG = Graphics.FromImage(MeasureBmp);
        static readonly Dictionary<string, float> CharCache = new Dictionary<string, float>();
        static readonly Dictionary<string, float> TokCache = new Dictionary<string, float>();

        static TextDoc()
        {
            MeasureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        const string NoStart = "，。、；：？！）》」』】…—,.;:?!)]}%”’·";
        const string NoEnd = "（《「『【([{“‘";

        public static float CharW(Font f, char c)
        {
            string key = f.Size.ToString("0.0") + c;
            float w;
            if (CharCache.TryGetValue(key, out w)) return w;
            w = MeasureG.MeasureString(c.ToString(), f, PointF.Empty, StringFormat.GenericTypographic).Width;
            CharCache[key] = w;
            return w;
        }

        public static float TokenW(Font f, string s)
        {
            if (s.Length == 1) return CharW(f, s[0]);
            string key = f.Size.ToString("0.0") + "|" + s;
            float w;
            if (TokCache.TryGetValue(key, out w)) return w;
            w = MeasureG.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
            TokCache[key] = w;
            return w;
        }

        public static float PrefixWidth(Painter p, Font f, string line, int count)
        {
            float w = 0;
            for (int i = 0; i < count && i < line.Length; i++) w += CharW(f, line[i]);
            return w;
        }

        // ------------------------------------------------------------ 编码
        public static string Decode(byte[] data, out string charset)
        {
            charset = "UTF-8";
            if (data == null || data.Length == 0) return "";

            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                charset = "UTF-8 BOM";
                return new UTF8Encoding(false).GetString(data, 3, data.Length - 3);
            }
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                charset = "UTF-16 LE";
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            }
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                charset = "UTF-16 BE";
                return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            }

            int zerosOdd = 0, zerosEven = 0, sample = Math.Min(data.Length, 8192);
            for (int i = 0; i < sample; i++)
            {
                if (data[i] == 0) { if ((i & 1) == 0) zerosEven++; else zerosOdd++; }
            }
            if (sample > 0 && (zerosOdd > sample * 0.25 || zerosEven > sample * 0.25))
            {
                if (zerosEven > zerosOdd) { charset = "UTF-16 LE"; return Encoding.Unicode.GetString(data); }
                charset = "UTF-16 BE";
                return Encoding.BigEndianUnicode.GetString(data);
            }

            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                string s = strict.GetString(data);
                charset = "UTF-8";
                return s;
            }
            catch { }

            try
            {
                Encoding gb = Encoding.GetEncoding(54936);
                charset = "GB18030";
                return gb.GetString(data);
            }
            catch { }

            charset = "GBK";
            return Encoding.GetEncoding(936).GetString(data);
        }

        // ------------------------------------------------------------ 分章
        static bool LooksLikeChapter(string line)
        {
            if (line.Length < 2 || line.Length > 40) return false;
            if (line.IndexOf('。') >= 0 || line.IndexOf('，') >= 0) return false;
            if (line.StartsWith("第"))
            {
                int idx = line.IndexOfAny(new char[] { '章', '节', '回', '卷', '篇', '集', '部' });
                if (idx > 0 && idx <= 8) return true;
            }
            string[] heads = { "序章", "楔子", "尾声", "后记", "番外", "前言", "引子" };
            for (int i = 0; i < heads.Length; i++)
                if (line.StartsWith(heads[i])) return true;
            if (line.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<Chapter> Split(string text)
        {
            return Split(text, null);
        }

        public static List<Chapter> Split(string text, string customRegex)
        {
            System.Text.RegularExpressions.Regex rx = null;
            if (!string.IsNullOrEmpty(customRegex))
            {
                try { rx = new System.Text.RegularExpressions.Regex(customRegex); } catch { rx = null; }
            }
            List<Chapter> list = new List<Chapter>();
            int n = text.Length;
            int lineStart = 0;
            int i = 0;
            while (i <= n)
            {
                if (i == n || text[i] == '\n')
                {
                    int end = i;
                    if (end > lineStart && text[end - 1] == '\r') end--;
                    int len = end - lineStart;
                    if (len > 0 && len <= 60)
                    {
                        string line = text.Substring(lineStart, len).Trim();
                        bool hit = rx != null ? rx.IsMatch(line) : LooksLikeChapter(line);
                        if (hit)
                            list.Add(new Chapter { Title = line, Start = lineStart, Length = 0 });
                    }
                    lineStart = i + 1;
                }
                i++;
            }

            if (list.Count == 0)
            {
                int pos = 0, part = 1;
                while (pos < n)
                {
                    int target = Math.Min(n, pos + 4000);
                    int cut = target;
                    if (target < n)
                    {
                        int nl = text.IndexOf('\n', target);
                        if (nl > 0 && nl - target < 800) cut = nl + 1;
                    }
                    list.Add(new Chapter { Title = "第 " + part + " 节", Start = pos, Length = cut - pos });
                    pos = cut;
                    part++;
                }
                if (list.Count == 0) list.Add(new Chapter { Title = "正文", Start = 0, Length = n });
                return list;
            }

            for (int k = 0; k < list.Count; k++)
            {
                int start = list[k].Start;
                int end = (k + 1 < list.Count) ? list[k + 1].Start : n;
                list[k].Length = Math.Max(0, end - start);
            }
            return list;
        }

        // ------------------------------------------------------------ 排版
        static bool IsAsciiToken(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '\'' || c == '-' || c == '_' || c == '%' || c == '.';
        }

        public static DocLayout Build(string text, float width, Font font,
            float lineH, float paraGap, float indentW, float letterSpacing = 0f)
        {
            DocLayout layout = new DocLayout();
            layout.Width = width;
            float y = 0f;
            int n = text.Length;
            int i = 0;

            while (i < n)
            {
                while (i < n && (text[i] == '\r' || text[i] == '\n')) i++;
                if (i >= n) break;

                int skip = i;
                while (skip < n && (text[skip] == '　' || text[skip] == ' ' || text[skip] == '\t')) skip++;
                i = skip;

                float x = indentW;
                int lineStart = i;
                bool firstLine = true;

                while (i < n && text[i] != '\n' && text[i] != '\r')
                {
                    int tEnd = i + 1;
                    if (IsAsciiToken(text[i]))
                    {
                        while (tEnd < n && IsAsciiToken(text[tEnd]) && text[tEnd] != '.') tEnd++;
                        if (tEnd < n && text[tEnd] == '.' && tEnd + 1 < n && char.IsDigit(text[tEnd + 1])) tEnd++;
                    }
                    string tok = text.Substring(i, tEnd - i);
                    float tw = TokenW(font, tok) + letterSpacing * tok.Length;

                    if (x + tw > width && i > lineStart)
                    {
                        char c0 = tok[0];
                        if (NoStart.IndexOf(c0) >= 0 && x + tw <= width + font.Size * 0.62f)
                        {
                            x += tw;
                            i = tEnd;
                            continue;
                        }
                        layout.Lines.Add(new LineInfo
                        {
                            Start = lineStart,
                            Len = i - lineStart,
                            Y = y,
                            H = lineH,
                            ParaStart = firstLine,
                            ParaEnd = false
                        });
                        y += lineH;
                        lineStart = i;
                        x = 0f;
                        firstLine = false;
                        continue;
                    }

                    x += tw;
                    i = tEnd;
                }

                layout.Lines.Add(new LineInfo
                {
                    Start = lineStart,
                    Len = Math.Max(0, i - lineStart),
                    Y = y,
                    H = lineH,
                    ParaStart = firstLine,
                    ParaEnd = true
                });
                y += lineH + paraGap;
            }

            layout.Height = Math.Max(0, y);
            return layout;
        }

        public static int LineAtY(DocLayout l, float y)
        {
            if (l == null || l.Lines.Count == 0) return 0;
            int lo = 0, hi = l.Lines.Count - 1, res = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (l.Lines[mid].Y <= y) { res = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return res;
        }

        public static int LineOfOffset(DocLayout l, int offset)
        {
            if (l == null || l.Lines.Count == 0) return 0;
            int lo = 0, hi = l.Lines.Count - 1, res = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (l.Lines[mid].Start <= offset) { res = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return res;
        }
    }
}
