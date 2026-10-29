using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Lamplight
{
    /// <summary>极简 HTML 树 + XPath 子集，用于兼容原版 Reader 的 bs.json 书源。</summary>
    public class HNode
    {
        public string Name = "";
        public Dictionary<string, string> Attrs = new Dictionary<string, string>();
        public List<HNode> Children = new List<HNode>();
        public HNode Parent;
        public string Text = "";

        public string Attr(string key)
        {
            string v;
            return Attrs.TryGetValue(key, out v) ? v : "";
        }

        public string InnerText()
        {
            if (Name == "#text") return Text;
            StringBuilder sb = new StringBuilder();
            AppendText(sb);
            return sb.ToString();
        }

        void AppendText(StringBuilder sb)
        {
            foreach (HNode c in Children)
            {
                if (c.Name == "#text") sb.Append(c.Text);
                else if (c.Name == "br") sb.Append('\n');
                else
                {
                    bool block = c.Name == "p" || c.Name == "div" || c.Name == "dd" || c.Name == "li" || c.Name == "h1" || c.Name == "h2" || c.Name == "h3";
                    c.AppendText(sb);
                    if (block) sb.Append('\n');
                }
            }
        }
    }

    public static class HtmlLite
    {
        static readonly HashSet<string> Void = new HashSet<string> { "br", "img", "hr", "input", "meta", "link", "area", "base", "col", "embed", "source", "track", "wbr" };
        static readonly HashSet<string> AutoClose = new HashSet<string> { "p", "li", "dd", "dt", "tr", "td", "th", "option" };

        public static HNode Parse(string html)
        {
            HNode root = new HNode { Name = "#root" };
            HNode cur = root;
            int i = 0, n = html.Length;
            while (i < n)
            {
                int lt = html.IndexOf('<', i);
                if (lt < 0) { AddText(cur, html.Substring(i)); break; }
                if (lt > i) AddText(cur, html.Substring(i, lt - i));
                if (lt + 1 >= n) break;
                char c = html[lt + 1];
                if (c == '!')
                {
                    int gt0 = html.IndexOf('>', lt);
                    i = gt0 < 0 ? n : gt0 + 1;
                    continue;
                }
                if (c == '/')
                {
                    int gt = html.IndexOf('>', lt);
                    if (gt < 0) break;
                    string name = html.Substring(lt + 2, Math.Max(0, gt - lt - 2)).Trim().ToLowerInvariant();
                    int sp = name.IndexOf(' ');
                    if (sp > 0) name = name.Substring(0, sp);
                    HNode p = cur;
                    while (p != null && p.Name != name) p = p.Parent;
                    if (p != null && p.Parent != null) cur = p.Parent;
                    i = gt + 1;
                    continue;
                }
                int end = FindTagEnd(html, lt);
                if (end < 0) { i = n; break; }
                string raw = html.Substring(lt + 1, end - lt - 1);
                bool selfClose = raw.EndsWith("/");
                if (selfClose) raw = raw.Substring(0, raw.Length - 1);
                string tag = raw;
                int sp2 = IndexOfSpace(tag);
                if (sp2 > 0) tag = tag.Substring(0, sp2);
                tag = tag.Trim().ToLowerInvariant();
                HNode node = new HNode { Name = tag, Parent = cur };
                ParseAttrs(raw, node.Attrs);
                cur.Children.Add(node);
                if (!selfClose && !Void.Contains(tag))
                {
                    if (AutoClose.Contains(tag)) cur = node;
                    else cur = node;
                }
                i = end + 1;
            }
            return root;
        }

        static void AddText(HNode cur, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string t = WebUtility.HtmlDecode(text);
            cur.Children.Add(new HNode { Name = "#text", Text = t, Parent = cur });
        }

        static int IndexOfSpace(string s)
        {
            for (int i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return i;
            return -1;
        }

        static int FindTagEnd(string html, int lt)
        {
            bool quote = false; char q = '"';
            for (int i = lt + 1; i < html.Length; i++)
            {
                char c = html[i];
                if (quote) { if (c == q) quote = false; }
                else if (c == '"' || c == '\'') { quote = true; q = c; }
                else if (c == '>') return i;
            }
            return -1;
        }

        static void ParseAttrs(string raw, Dictionary<string, string> attrs)
        {
            Regex rx = new Regex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))");
            foreach (Match m in rx.Matches(raw))
            {
                string val = m.Groups[3].Success ? m.Groups[3].Value : (m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value);
                attrs[m.Groups[1].Value.ToLowerInvariant()] = WebUtility.HtmlDecode(val);
            }
        }

        static IEnumerable<HNode> Descendants(HNode n)
        {
            foreach (HNode c in n.Children)
            {
                if (c.Name == "#text") continue;
                yield return c;
                foreach (HNode d in Descendants(c)) yield return d;
            }
        }

        /// <summary>支持 //tag[@class='x']/a/@href、//*[@id="content"]、text() 等常用写法。</summary>
        public static List<HNode> Select(HNode root, string xpath)
        {
            List<HNode> cur = new List<HNode> { root };
            if (string.IsNullOrEmpty(xpath)) return cur;
            string path = xpath.Trim();
            bool attrResult = false;
            string attrName = null;
            if (path.EndsWith("/@")) { }
            int attrAt = path.LastIndexOf("/@", StringComparison.Ordinal);
            if (attrAt >= 0) { attrName = path.Substring(attrAt + 2); path = path.Substring(0, attrAt); attrResult = true; }
            if (!attrResult && path.EndsWith("/text()")) { path = path.Substring(0, path.Length - 7); }

            // 解析成 (步, 是否后代) 序列，支持 //a//b 这种中间后代
            List<string> steps = new List<string>();
            List<bool> desc = new List<bool>();
            int pos = 0;
            while (pos < path.Length)
            {
                bool isDesc = false;
                int slash = 0;
                while (pos < path.Length && path[pos] == '/') { slash++; pos++; }
                if (slash == 0) { pos++; continue; }
                isDesc = slash >= 2;
                int startPos = pos;
                while (pos < path.Length && path[pos] != '/') pos++;
                string step = path.Substring(startPos, pos - startPos).Trim();
                if (step.Length == 0) continue;
                steps.Add(step);
                desc.Add(isDesc);
            }
            for (int si = 0; si < steps.Count; si++)
            {
                string step = steps[si];
                bool fromDesc = desc[si];
                List<HNode> next = new List<HNode>();
                foreach (HNode node in cur)
                {
                    IEnumerable<HNode> pool = fromDesc ? Descendants(node) : ChildrenOf(node);
                    foreach (HNode cand in pool)
                        if (Match(cand, step)) next.Add(cand);
                }
                cur = next;
                if (cur.Count == 0) break;
            }
            return cur;
        }

        static IEnumerable<HNode> ChildrenOf(HNode n)
        {
            foreach (HNode c in n.Children) if (c.Name != "#text") yield return c;
        }

        static bool Match(HNode n, string step)
        {
            int br = step.IndexOf('[');
            string tag = br >= 0 ? step.Substring(0, br) : step;
            tag = tag.Trim().ToLowerInvariant();
            if (tag.Length > 0 && tag != "*" && n.Name != tag) return false;
            if (br >= 0)
            {
                int close = step.LastIndexOf(']');
                if (close > br)
                {
                    string pred = step.Substring(br + 1, close - br - 1).Trim();
                    if (!MatchPred(n, pred)) return false;
                }
            }
            return true;
        }

        static bool MatchPred(HNode n, string pred)
        {
            Match m = Regex.Match(pred, @"^@([\w-]+)\s*=\s*['""]?([^'""\]]*)['""]?$");
            if (m.Success) return string.Equals(n.Attr(m.Groups[1].Value), m.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
            m = Regex.Match(pred, @"^contains\s*\(\s*@([\w-]+)\s*,\s*['""]([^'""]*)['""]\s*\)$", RegexOptions.IgnoreCase);
            if (m.Success) return n.Attr(m.Groups[1].Value).IndexOf(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase) >= 0;
            int num;
            if (int.TryParse(pred, out num)) return num >= 1;
            return true;
        }

        /// <summary>直接取值：末尾 /@attr 取属性，其余取内部文本。</summary>
        public static List<string> Values(HNode root, string xpath)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(xpath)) return list;
            string attr = null;
            string path = xpath.Trim();
            int at = path.LastIndexOf("/@", StringComparison.Ordinal);
            if (at >= 0) { attr = path.Substring(at + 2); path = path.Substring(0, at); }
            else if (path.EndsWith("/text()")) path = path.Substring(0, path.Length - 7);
            foreach (HNode n in Select(root, path))
            {
                string v = attr != null ? n.Attr(attr) : n.InnerText();
                if (!string.IsNullOrEmpty(v)) list.Add(v.Trim());
            }
            return list;
        }

        public static string Value(HNode n, string attr)
        {
            if (n == null) return "";
            return string.IsNullOrEmpty(attr) ? n.InnerText() : n.Attr(attr);
        }
    }
}
