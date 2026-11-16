using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Lamplight
{
    /// <summary>
    /// 「阅读（Legado）」书源转换：把 CSS 风格规则尽力翻译成引擎能用的 XPath 子集。
    /// 只覆盖常见写法；遇到 JS / JSONPath / 复杂选择器会跳过并给出原因。
    /// </summary>
    static class Legado
    {
        public static bool LooksLegado(string text)
        {
            return text != null && text.IndexOf("bookSourceName", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<SourceDef> Convert(string json, List<string> issues)
        {
            List<SourceDef> list = new List<SourceDef>();
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            object root = ser.Deserialize<object>(json);
            System.Collections.IEnumerable arr = null;
            Dictionary<string, object> rootObj = root as Dictionary<string, object>;
            if (rootObj != null && rootObj.ContainsKey("bookSourceName"))
            {
                // 单条书源：{ "bookSourceName": ... }
                arr = new object[] { rootObj };
            }
            else if (rootObj != null)
            {
                // 有些导出会包一层：{ "bookSources": [...] }
                foreach (string key in new string[] { "bookSources", "sources", "data" })
                {
                    object inner;
                    if (rootObj.TryGetValue(key, out inner) && inner is System.Collections.IEnumerable)
                    {
                        arr = (System.Collections.IEnumerable)inner;
                        break;
                    }
                }
            }
            else
            {
                arr = root as System.Collections.IEnumerable;
            }
            if (arr == null) { issues.Add("不是书源数组"); return list; }

            int idx = 0;
            foreach (object o in arr)
            {
                idx++;
                Dictionary<string, object> m = o as Dictionary<string, object>;
                if (m == null) continue;
                string name = Str(m, "bookSourceName");
                string home = Str(m, "bookSourceUrl");
                if (string.IsNullOrEmpty(name)) name = "书源 " + idx;

                Dictionary<string, object> rs = Obj(m, "ruleSearch");
                Dictionary<string, object> rt = Obj(m, "ruleToc");
                Dictionary<string, object> rc = Obj(m, "ruleContent");
                string searchUrl = Str(m, "searchUrl");

                if (string.IsNullOrEmpty(searchUrl) || searchUrl.IndexOf("@js:", StringComparison.OrdinalIgnoreCase) >= 0)
                { issues.Add("第 " + idx + " 条（" + name + "）搜索地址是 JS，跳过"); continue; }

                string urlPart = searchUrl;
                string method = "GET", body = "";
                int comma = FindOptionsComma(searchUrl);
                if (comma > 0)
                {
                    urlPart = searchUrl.Substring(0, comma).Trim();
                    string opts = searchUrl.Substring(comma + 1).Trim();
                    if (opts.StartsWith("{"))
                    {
                        try
                        {
                            Dictionary<string, object> om = ser.Deserialize<Dictionary<string, object>>(opts);
                            string mm = Str(om, "method");
                            if (!string.IsNullOrEmpty(mm)) method = mm.ToUpperInvariant();
                            body = Str(om, "body");
                        }
                        catch { }
                    }
                }

                string listXp, why;
                listXp = ToXPath(Str(rs, "bookList"), null, out why);
                if (listXp == null) { issues.Add("第 " + idx + " 条（" + name + "）列表规则不支持：" + why); continue; }
                string nameXp = ToXPath(Str(rs, "name"), listXp, out why);
                string hrefXp = ToXPath(Str(rs, "bookUrl"), listXp, out why);
                string tocXp = ToXPath(Str(rt, "chapterList"), null, out why);
                if (tocXp == null) { issues.Add("第 " + idx + " 条（" + name + "）目录规则不支持：" + why); continue; }
                string chapNameXp = ToXPath(Str(rt, "chapterName"), tocXp, out why);
                string chapUrlXp = ToXPath(Str(rt, "chapterUrl"), tocXp, out why);
                string contentXp = ToXPath(Str(rc, "content"), null, out why);
                if (contentXp == null) { issues.Add("第 " + idx + " 条（" + name + "）正文规则不支持：" + why); continue; }

                if (nameXp == null || hrefXp == null || chapNameXp == null || chapUrlXp == null)
                {
                    string miss = (nameXp == null ? "书名 " : "") + (hrefXp == null ? "书页 " : "")
                                + (chapNameXp == null ? "章节名 " : "") + (chapUrlXp == null ? "章节地址" : "");
                    issues.Add("第 " + idx + " 条（" + name + "）字段规则不完整：" + miss.Trim());
                    continue;
                }

                SourceDef sd = new SourceDef();
                sd.Name = name;
                sd.Host = home;
                sd.QueryUrl = urlPart;
                sd.QueryMethod = method == "POST" ? 1 : 0;
                sd.QueryBody = body;
                sd.BookNameXPath = nameXp;
                sd.BookHrefXPath = hrefXp;
                sd.BookAuthorXPath = ToXPath(Str(rs, "author"), listXp, out why) ?? "";
                sd.ChapterTitleXPath = chapNameXp;
                sd.ChapterUrlXPath = chapUrlXp;
                sd.ContentXPath = contentXp;
                sd.Charset = "utf-8";
                sd.Id = Store.MakeId(sd.Name + sd.Host);
                sd.Enabled = true;
                list.Add(sd);
            }
            return list;
        }

        static int FindOptionsComma(string s)
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '{' || s[i] == '[') depth++;
                else if (s[i] == '}' || s[i] == ']') depth--;
                else if (s[i] == ',' && depth == 0 && i + 1 < s.Length && s[i + 1] == '{') return i;
            }
            return -1;
        }

        /// <summary>把一条 Legado 规则翻译成 XPath；baseXp 用于「相对上一条列表」的字段。</summary>
        public static string ToXPath(string rule, string baseXp, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(rule)) { why = "空"; return null; }
            string r = rule.Trim();
            if (r.StartsWith("@css:", StringComparison.OrdinalIgnoreCase)) r = r.Substring(5).Trim();
            else if (r.StartsWith("css:", StringComparison.OrdinalIgnoreCase)) r = r.Substring(4).Trim();
            if (r.IndexOf("@js:", StringComparison.OrdinalIgnoreCase) >= 0 || r.IndexOf("<js>", StringComparison.OrdinalIgnoreCase) >= 0)
            { why = "含 JS"; return null; }
            if (r.IndexOf("@json:", StringComparison.OrdinalIgnoreCase) >= 0) { why = "JSONPath"; return null; }
            int alt = r.IndexOf("||", StringComparison.Ordinal);
            if (alt >= 0) r = r.Substring(0, alt).Trim();
            int clean = r.IndexOf("##", StringComparison.Ordinal);
            if (clean >= 0) r = r.Substring(0, clean).Trim();
            if (r.Length == 0) { why = "空（只有清洗规则）"; return null; }

            string[] steps = r.Split('@');
            StringBuilder sb = new StringBuilder();
            bool started = false;
            for (int i = 0; i < steps.Length; i++)
            {
                string st = steps[i].Trim();
                if (st.Length == 0) continue;
                if (IsExtract(st))
                {
                    if (!started && baseXp != null) sb.Append(baseXp);
                    sb.Append(ExtractPath(st));
                    started = true;
                    continue;
                }
                if (Regex.IsMatch(st, @"^!\d+$") || st.IndexOf("[*]", StringComparison.Ordinal) >= 0)
                { why = "复杂选择器 " + st; return null; }

                string css = CssToXPath(st, out why);
                if (css == null) return null;
                if (!started)
                {
                    sb.Append(baseXp != null ? baseXp + "//" + css : "//" + css);
                    started = true;
                }
                else sb.Append("//" + css);
            }
            if (!started) { why = "空"; return null; }
            return sb.ToString();
        }

        static bool IsExtract(string s)
        {
            return s == "text" || s == "textNodes" || s == "@text" || s == "href" || s == "@href"
                || s == "src" || s == "@src" || s == "html" || s == "@html";
        }

        static string ExtractPath(string s)
        {
            if (s == "href" || s == "@href") return "/@href";
            if (s == "src" || s == "@src") return "/@src";
            return "/text()";
        }

        static string CssToXPath(string sel, out string why)
        {
            why = "";
            string work = sel.Trim();
            // 去掉 Legado 的排除语法 !0 / !1:2
            work = Regex.Replace(work, @"![\d:]+", "");
            // class. 后面可能跟多个空格分隔的 class（部分书源写成 class. A B）
            work = Regex.Replace(work, @"class\.\s*([^\s@>]+(?:\s+[^\s@>]+)*)", delegate(Match m)
            {
                return "class." + m.Groups[1].Value.Trim().Replace(" ", ".");
            });
            if (work.StartsWith("id.", StringComparison.OrdinalIgnoreCase))
                work = "#" + work.Substring(3);
            else if (work.StartsWith("class.", StringComparison.OrdinalIgnoreCase))
                work = "." + work.Substring(6);
            else if (work.StartsWith("tag.", StringComparison.OrdinalIgnoreCase))
                work = work.Substring(4);
            if (work.Length == 0) { why = "只有排除规则"; return null; }

            List<string> alts = SplitTopComma(work);
            if (alts.Count > 1)
            {
                StringBuilder ub = new StringBuilder("*[");
                for (int ai = 0; ai < alts.Count; ai++)
                {
                    string one = OneCss(alts[ai].Trim(), out why);
                    if (one == null) return null;
                    if (ai > 0) ub.Append(" or ");
                    ub.Append("self::" + one);
                }
                ub.Append("]");
                return ub.ToString();
            }

            List<string> parts = SplitTopLevel(work);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                string p = parts[i];
                if (p.Length == 0) continue;
                if (p == ">") continue;
                string one = OneCss(p, out why);
                if (one == null) return null;
                if (i > 0) sb.Append("//");
                sb.Append(one);
            }
            if (sb.Length == 0) { why = "空"; return null; }
            return sb.ToString();
        }

        /// <summary>按顶层空格拆分选择器，避免把 [class=a b] 拆开。</summary>
        static List<string> SplitTopLevel(string s)
        {
            List<string> list = new List<string>();
            StringBuilder cur = new StringBuilder();
            int depth = 0;
            foreach (char c in s)
            {
                if (c == '[' || c == '(') depth++;
                else if (c == ']' || c == ')') depth = Math.Max(0, depth - 1);
                if ((c == ' ' || c == '>') && depth == 0)
                {
                    if (cur.Length > 0) { list.Add(cur.ToString()); cur.Length = 0; }
                    if (c == '>') list.Add(">");
                }
                else cur.Append(c);
            }
            if (cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        /// <summary>按顶层逗号拆分联合选择器（a,dt）。</summary>
        static List<string> SplitTopComma(string s)
        {
            List<string> list = new List<string>();
            StringBuilder cur = new StringBuilder();
            int depth = 0;
            foreach (char c in s)
            {
                if (c == '[' || c == '(') depth++;
                else if (c == ']' || c == ')') depth = Math.Max(0, depth - 1);
                if (c == ',' && depth == 0)
                {
                    if (cur.Length > 0) { list.Add(cur.ToString()); cur.Length = 0; }
                }
                else cur.Append(c);
            }
            if (cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        static string OneCss(string p, out string why)
        {
            why = "";
            string s = p.Trim();
            int trailing = -1;
            Match mIdx = Regex.Match(s, @"^(.*)\.(\d+)$");
            if (mIdx.Success)
            {
                s = mIdx.Groups[1].Value;
                int.TryParse(mIdx.Groups[2].Value, out trailing);
            }
            Match m = Regex.Match(s, @"^([a-zA-Z0-9*_-]*)(.*)$");
            if (!m.Success) { why = "选择器 " + p; return null; }
            string tag = m.Groups[1].Value;
            string rest = m.Groups[2].Value;
            if (tag.Length == 0) tag = "*";

            List<string> conds = new List<string>();
            List<string> poss = new List<string>();
            int i = 0;
            while (i < rest.Length)
            {
                char c = rest[i];
                if (c == '#' || c == '.')
                {
                    Match mm = Regex.Match(rest.Substring(i + 1), @"^[\w-]+");
                    if (!mm.Success) { why = "选择器 " + p; return null; }
                    string name = mm.Value;
                    conds.Add(c == '#' ? "@id=\"" + name + "\"" : "contains(@class,\"" + name + "\")");
                    i += 1 + name.Length;
                }
                else if (c == '[')
                {
                    int close = rest.IndexOf(']', i);
                    if (close < 0) { why = "选择器 " + p; return null; }
                    string inner = rest.Substring(i + 1, close - i - 1).Trim();
                    string sub;
                    if (!AttrCond(inner, conds, poss, out sub)) { why = "选择器 " + p + "（" + sub + "）"; return null; }
                    i = close + 1;
                }
                else if (c == ':')
                {
                    Match pm = Regex.Match(rest.Substring(i), @"^:([a-zA-Z-]+)(?:\(([^)]*)\))?");
                    if (!pm.Success) { why = "选择器 " + p; return null; }
                    string name = pm.Groups[1].Value.ToLowerInvariant();
                    string arg = pm.Groups[2].Value.Trim();
                    if (name == "eq")
                    {
                        int n;
                        if (!int.TryParse(arg, out n)) { why = ":eq 参数 " + arg; return null; }
                        poss.Add("[" + (n + 1) + "]");
                    }
                    else if (name == "nth-child" || name == "nth-of-type")
                    {
                        string pred = NthPredicate(arg);
                        if (pred == null) { why = ":" + name + " 参数 " + arg; return null; }
                        poss.Add(pred);
                    }
                    else if (name == "first-child" || name == "first") poss.Add("[1]");
                    else if (name == "last-child" || name == "last") poss.Add("[last()]");
                    else if (name == "lt")
                    {
                        int n; if (!int.TryParse(arg, out n)) { why = ":lt 参数"; return null; }
                        poss.Add("[position() <= " + n + "]");
                    }
                    else if (name == "gt")
                    {
                        int n; if (!int.TryParse(arg, out n)) { why = ":gt 参数"; return null; }
                        poss.Add("[position() > " + (n + 1) + "]");
                    }
                    else if (name == "not")
                    {
                        string a = arg.Trim();
                        if (a.StartsWith(".")) conds.Add("not(contains(@class,\"" + a.Substring(1).Trim() + "\"))");
                        else if (a.StartsWith("#")) conds.Add("not(@id=\"" + a.Substring(1).Trim() + "\")");
                        else if (a.Length > 0) conds.Add("not(self::" + a + ")");
                        else { why = ":not 参数为空"; return null; }
                    }
                    else if (name == "has")
                    {
                        string a = arg.Trim().TrimStart('>').Trim();
                        if (a.Length == 0) { why = ":has 参数为空"; return null; }
                        conds.Add(".//" + a);
                    }
                    else if (name == "contains")
                    {
                        string a = arg.Trim().Trim('\'', '"');
                        conds.Add("contains(.,\"" + a + "\")");
                    }
                    else { why = "伪类 :" + name; return null; }
                    i += pm.Length;
                }
                else if (char.IsWhiteSpace(c)) i++;
                else { why = "选择器 " + p; return null; }
            }

            StringBuilder outp = new StringBuilder(tag);
            if (conds.Count > 0) outp.Append("[" + string.Join(" and ", conds.ToArray()) + "]");
            foreach (string q in poss) outp.Append(q);
            if (trailing >= 0) outp.Append("[" + (trailing + 1) + "]");
            return outp.ToString();
        }

        static string NthPredicate(string arg)
        {
            arg = (arg ?? "").Trim().ToLowerInvariant();
            if (arg == "even") return "[position() mod 2 = 0]";
            if (arg == "odd") return "[position() mod 2 = 1]";
            Match m = Regex.Match(arg, @"^(\d*)n(?:([+-]\d+))?$");
            if (m.Success)
            {
                int a = m.Groups[1].Value.Length == 0 ? 1 : int.Parse(m.Groups[1].Value);
                int b = m.Groups[2].Value.Length == 0 ? 0 : int.Parse(m.Groups[2].Value);
                if (a == 0) return "[position() = " + b + "]";
                if (a == 1) return b > 0 ? "[position() >= " + b + "]" : "[position() >= 1]";
                int rem = ((b % a) + a) % a;
                return "[position() mod " + a + " = " + rem + "]" + (b > 0 ? "[position() >= " + b + "]" : "");
            }
            int n;
            if (int.TryParse(arg, out n)) return "[position() = " + n + "]";
            return null;
        }

        static bool AttrCond(string inner, List<string> conds, List<string> poss, out string why)
        {
            why = "";
            Match slice = Regex.Match(inner, @"^(-?\d*):(-?\d*)$");
            if (slice.Success)
            {
                int start, end;
                int.TryParse(slice.Groups[1].Value, out start);
                int.TryParse(slice.Groups[2].Value, out end);
                List<string> ps = new List<string>();
                if (start > 0) ps.Add("position() > " + start);
                if (end < 0) ps.Add(end == -1 ? "position() < last()" : "position() < last() - " + (-end - 1));
                else if (end > 0) ps.Add("position() <= " + end);
                if (ps.Count > 0) poss.Add("[" + string.Join(" and ", ps.ToArray()) + "]");
                return true;
            }
            if (inner.IndexOf("~=", StringComparison.Ordinal) >= 0)
            {
                why = "属性正则";
                return false;
            }
            int eq = inner.IndexOf('=');
            if (eq < 0)
            {
                string attr = inner.Trim();
                if (attr.Length == 0) { why = "空属性"; return false; }
                conds.Add("@" + attr);
                return true;
            }
            string op = "=";
            int at = eq;
            if (eq > 0)
            {
                char prev = inner[eq - 1];
                if (prev == '^' || prev == '$' || prev == '*' || prev == '~' || prev == '!')
                {
                    op = prev + "=";
                    at = eq - 1;
                }
            }
            string key = inner.Substring(0, at).Trim();
            string val = inner.Substring(eq + 1).Trim().Trim('\'', '"');
            if (key.Length == 0) { why = "空属性"; return false; }
            if (op == "=") conds.Add("@" + key + "=\"" + val + "\"");
            else if (op == "^=") conds.Add("starts-with(@" + key + ",\"" + val + "\")");
            else if (op == "*=") conds.Add("contains(@" + key + ",\"" + val + "\")");
            else if (op == "$=") conds.Add("substring(@" + key + ", string-length(@" + key + ") - " + val.Length + " + 1) = \"" + val + "\"");
            else
            {
                why = "属性操作 " + op;
                return false;
            }
            return true;
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> m, string key)
        {
            object v;
            if (m.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        static string Str(Dictionary<string, object> m, string key)
        {
            if (m == null) return "";
            object v;
            return m.TryGetValue(key, out v) && v != null ? System.Convert.ToString(v) : "";
        }
    }
}
