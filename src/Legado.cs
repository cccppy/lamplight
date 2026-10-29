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
            System.Collections.IEnumerable arr = root as System.Collections.IEnumerable;
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
                { issues.Add("第 " + idx + " 条（" + name + "）字段规则不完整"); continue; }

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
                    sb.Append(ExtractPath(st));
                    started = true;
                    continue;
                }
                if (Regex.IsMatch(st, @"^!\d+$") || st.IndexOf(":has", StringComparison.Ordinal) >= 0
                    || st.IndexOf(":lt", StringComparison.Ordinal) >= 0 || st.IndexOf(":gt", StringComparison.Ordinal) >= 0
                    || st.IndexOf("[*]", StringComparison.Ordinal) >= 0)
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
            // 处理 id.xxx / class.xxx / tag.xxx
            string work = sel.Trim();
            // 去掉 Legado 的排除/切片语法：!0、!1:2、:1:2、[1:-1]
            work = Regex.Replace(work, @"![\d:]+", "");
            work = Regex.Replace(work, @"\[[-\d:]+\]", "");
            work = Regex.Replace(work, @":\d+(?=[\s.]|$)", "");
            if (work.Length == 0) { why = "只有排除规则"; return null; }
            if (work.StartsWith("id.", StringComparison.OrdinalIgnoreCase))
                work = "#" + work.Substring(3);
            else if (work.StartsWith("class.", StringComparison.OrdinalIgnoreCase))
                work = "." + work.Substring(6);
            else if (work.StartsWith("tag.", StringComparison.OrdinalIgnoreCase))
                work = work.Substring(4);

            string[] parts = work.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p == ">") continue;
                string one = OneCss(p, out why);
                if (one == null) return null;
                if (i > 0) sb.Append("//");
                sb.Append(one);
            }
            return sb.ToString();
        }

        static string OneCss(string p, out string why)
        {
            why = "";
            string s = p;
            // 去掉末尾索引 .0 / .1
            Match mIdx = Regex.Match(s, @"^(.*)\.(\d+)$");
            string idx = "";
            if (mIdx.Success)
            {
                s = mIdx.Groups[1].Value;
                idx = "";
            }
            Match m = Regex.Match(s, @"^([a-zA-Z0-9*_-]*)((?:[#.]?[\w-]+)*)$");
            if (!m.Success) { why = "选择器 " + p; return null; }
            string tag = m.Groups[1].Value;
            string rest = m.Groups[2].Value;
            if (tag.Length == 0) tag = "*";
            List<string> conds = new List<string>();
            if (rest.Length > 0)
            {
                foreach (Match mm in Regex.Matches(rest, @"([#.])([\w-]+)"))
                {
                    if (mm.Groups[1].Value == "#") conds.Add("@id=\"" + mm.Groups[2].Value + "\"");
                    else conds.Add("contains(@class,\"" + mm.Groups[2].Value + "\")");
                }
            }
            string outp = tag;
            if (conds.Count > 0) outp += "[" + string.Join(" and ", conds.ToArray()) + "]";
            return outp + idx;
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
