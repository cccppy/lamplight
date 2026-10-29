using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Lamplight
{
    /// <summary>EPUB 解析：container.xml → OPF → spine → XHTML 转纯文本。</summary>
    static class Epub
    {
        public static bool IsEpub(string path)
        {
            return path != null && path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Load(string path, out string bookText, out List<Chapter> chapters, out string title, out string error)
        {
            bookText = "";
            chapters = new List<Chapter>();
            title = Path.GetFileNameWithoutExtension(path);
            error = null;
            try
            {
                using (FileStream fs = File.OpenRead(path))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    string opfPath = FindOpf(zip);
                    if (opfPath == null) { error = "不是有效的 EPUB（缺少 container.xml）"; return false; }
                    ZipArchiveEntry opfEntry = zip.GetEntry(opfPath);
                    if (opfEntry == null) { error = "OPF 文件不存在"; return false; }
                    XDocument opf = XDocument.Load(opfEntry.Open());
                    string baseDir = Path.GetDirectoryName(opfPath.Replace('/', '\\')) ?? "";

                    string t = Value(opf, "title");
                    if (!string.IsNullOrEmpty(t)) title = t.Trim();

                    Dictionary<string, string> manifest = new Dictionary<string, string>();
                    Dictionary<string, string> media = new Dictionary<string, string>();
                    foreach (XElement e in Desc(opf, "item"))
                    {
                        string id = Attr(e, "id"), href = Attr(e, "href"), mt = Attr(e, "media-type");
                        if (id != null && href != null)
                        {
                            manifest[id] = href;
                            media[id] = mt ?? "";
                        }
                    }

                    List<string> order = new List<string>();
                    foreach (XElement e in Desc(opf, "itemref"))
                    {
                        string idref = Attr(e, "idref");
                        if (idref != null && manifest.ContainsKey(idref)) order.Add(idref);
                    }
                    if (order.Count == 0) { error = "EPUB 没有可读章节（spine 为空）"; return false; }

                    StringBuilder all = new StringBuilder();
                    int idx = 0;
                    foreach (string id in order)
                    {
                        string mt = media.ContainsKey(id) ? media[id] : "";
                        if (mt.IndexOf("html", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string href = manifest[id];
                        string entryPath = Combine(baseDir, href);
                        ZipArchiveEntry entry = FindEntry(zip, entryPath);
                        if (entry == null) continue;
                        string html = ReadText(entry);
                        string chapTitle = ExtractTitle(html);
                        if (string.IsNullOrEmpty(chapTitle)) chapTitle = "第 " + (++idx) + " 节";
                        else idx++;
                        string text = HtmlToText(html).Trim();
                        if (text.Length == 0) continue;
                        // 去掉正文开头与章节名重复的标题行
                        if (text.StartsWith(chapTitle, StringComparison.Ordinal))
                        {
                            int nl0 = text.IndexOf('\n');
                            if (nl0 > 0 && nl0 < chapTitle.Length + 40) text = text.Substring(nl0).Trim();
                            else text = text.Substring(chapTitle.Length).Trim();
                        }

                        string block = chapTitle + "\n\n" + text + "\n\n";
                        int start = all.Length;
                        all.Append(block);
                        chapters.Add(new Chapter { Title = chapTitle, Start = start, Length = block.Length });
                    }
                    if (chapters.Count == 0) { error = "没有解析到正文"; return false; }
                    bookText = all.ToString();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static IEnumerable<XElement> Desc(XDocument d, string local)
        {
            foreach (XElement e in d.Descendants())
                if (string.Equals(e.Name.LocalName, local, StringComparison.OrdinalIgnoreCase)) yield return e;
        }

        static IEnumerable<XElement> Desc(XElement d, string local)
        {
            foreach (XElement e in d.Descendants())
                if (string.Equals(e.Name.LocalName, local, StringComparison.OrdinalIgnoreCase)) yield return e;
        }

        static string Attr(XElement e, string name)
        {
            foreach (XAttribute a in e.Attributes())
                if (string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)) return a.Value;
            return null;
        }

        static string Value(XDocument doc, string local)
        {
            foreach (XElement e in Desc(doc, local)) return e.Value;
            return "";
        }

        static string FindOpf(ZipArchive zip)
        {
            ZipArchiveEntry c = zip.GetEntry("META-INF/container.xml");
            if (c == null) return null;
            try
            {
                XDocument doc = XDocument.Load(c.Open());
                foreach (XElement e in doc.Descendants())
                    if (string.Equals(e.Name.LocalName, "rootfile", StringComparison.OrdinalIgnoreCase))
                    {
                        string full = Attr(e, "full-path");
                        if (!string.IsNullOrEmpty(full)) return full;
                    }
            }
            catch { }
            return null;
        }

        static ZipArchiveEntry FindEntry(ZipArchive zip, string path)
        {
            ZipArchiveEntry e = zip.GetEntry(path);
            if (e != null) return e;
            string norm = path.Replace('\\', '/');
            e = zip.GetEntry(norm);
            if (e != null) return e;
            foreach (ZipArchiveEntry x in zip.Entries)
                if (string.Equals(x.FullName.Replace('\\', '/'), norm, StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }

        static string Combine(string baseDir, string href)
        {
            href = Uri.UnescapeDataString(href);
            int hash = href.IndexOf('#');
            if (hash >= 0) href = href.Substring(0, hash);
            string p = string.IsNullOrEmpty(baseDir) ? href : baseDir + "\\" + href;
            p = p.Replace('/', '\\');
            while (p.Contains("\\\\")) p = p.Replace("\\\\", "\\");
            return p;
        }

        static string ReadText(ZipArchiveEntry entry)
        {
            using (Stream s = entry.Open())
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                byte[] data = ms.ToArray();
                string utf8;
                try { utf8 = new UTF8Encoding(false, true).GetString(data); }
                catch { utf8 = Encoding.GetEncoding(54936).GetString(data); }
                return utf8;
            }
        }

        static string ExtractTitle(string html)
        {
            Match m = Regex.Match(html, @"(?is)<title[^>]*>(.*?)</title>");
            if (m.Success)
            {
                string s = WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\s+", " ")).Trim();
                if (s.Length > 0 && s.Length < 60) return s;
            }
            m = Regex.Match(html, @"(?is)<h[1-3][^>]*>(.*?)</h[1-3]>");
            if (m.Success)
            {
                string s = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(m.Groups[1].Value, @"<[^>]+>", ""), @"\s+", " ")).Trim();
                if (s.Length > 0) return s;
            }
            return null;
        }

        static string HtmlToText(string html)
        {
            string s = Regex.Replace(html, @"(?is)<head.*?</head>", "");
            s = Regex.Replace(s, @"(?is)<script.*?</script>", "");
            s = Regex.Replace(s, @"(?is)<style.*?</style>", "");
            s = Regex.Replace(s, @"(?i)<br\s*/?>", "\n");
            s = Regex.Replace(s, @"(?i)</(p|div|h[1-6]|li|tr)\s*>", "\n\n");
            s = Regex.Replace(s, @"(?is)<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            s = s.Replace("\r", "");
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @" *\n *", "\n");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }
    }
}
