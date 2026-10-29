using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Lamplight
{
    public class SourceDef
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public string Charset { get; set; }
        public string SearchUrl { get; set; }        // {q} 关键词，{page} 页码
        public string SearchItemRegex { get; set; }  // 命名组 title / url / author
        public string ChapterListRegex { get; set; } // 命名组 title / url
        public string ContentRegex { get; set; }     // 命名组 content
        public bool Enabled { get; set; }
        // ---- 原版 bs.json（XPath）字段
        public string QueryUrl { get; set; }
        public int QueryMethod { get; set; }
        public string QueryBody { get; set; }
        public int QueryCharset { get; set; }
        public string BookNameXPath { get; set; }
        public string BookHrefXPath { get; set; }
        public string BookAuthorXPath { get; set; }
        public string ChapterTitleXPath { get; set; }
        public string ChapterUrlXPath { get; set; }
        public string ContentXPath { get; set; }

        public bool IsXPath { get { return !string.IsNullOrEmpty(ContentXPath); } }

        public SourceDef() { Charset = "utf-8"; Enabled = true; }
    }

    public class OnlineBook
    {
        public string Title;
        public string Author;
        public string Url;
        public string SourceId;
        public string SourceName;
    }

    /// <summary>在线书源：搜索、目录、正文抓取与本地缓存。</summary>
    public static class Online
    {
        public static List<SourceDef> Sources = new List<SourceDef>();
        public static string SourceFile;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static void Init(string dataDir)
        {
            SourceFile = Path.Combine(dataDir, "sources.json");
            try
            {
                if (File.Exists(SourceFile))
                {
                    string s = File.ReadAllText(SourceFile, Encoding.UTF8);
                    List<SourceDef> list = Json.Deserialize<List<SourceDef>>(s);
                    if (list != null) Sources = list;
                }
                if ((Sources == null || Sources.Count == 0) && File.Exists(SourceFile + ".bak"))
                {
                    List<SourceDef> bak = Json.Deserialize<List<SourceDef>>(File.ReadAllText(SourceFile + ".bak", Encoding.UTF8));
                    if (bak != null && bak.Count > 0) Sources = bak;
                }
            }
            catch { Sources = new List<SourceDef>(); }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SourceFile));
                File.WriteAllText(SourceFile, Json.Serialize(Sources), new UTF8Encoding(false));
            }
            catch { }
        }

        public static SourceDef Find(string id)
        {
            foreach (SourceDef s in Sources) if (s.Id == id) return s;
            return null;
        }

        public static List<string> LastIssues = new List<string>();

        /// <summary>解析书源 JSON：兼容 book_sources / sources / 根数组，字段名大小写与常见别名。</summary>
        public static List<SourceDef> ParseBsJson(string text)
        {
            LastIssues.Clear();
            List<SourceDef> list = new List<SourceDef>();
            JavaScriptSerializer json = new JavaScriptSerializer();
            json.MaxJsonLength = int.MaxValue;
            object rootObj = json.Deserialize<object>(text);
            System.Collections.IEnumerable arr = null;

            Dictionary<string, object> root = rootObj as Dictionary<string, object>;
            if (root != null)
            {
                string[] keys = { "book_sources", "sources", "source_list", "data", "list", "items" };
                foreach (string k in keys)
                {
                    object v;
                    if (root.TryGetValue(k, out v) && v != null)
                    {
                        Dictionary<string, object> nested = v as Dictionary<string, object>;
                        if (nested != null)
                        {
                            object inner;
                            if (nested.TryGetValue("list", out inner) || nested.TryGetValue("sources", out inner))
                                arr = inner as System.Collections.IEnumerable;
                        }
                        else arr = v as System.Collections.IEnumerable;
                        if (arr != null) break;
                    }
                }
                if (arr == null)
                {
                    foreach (KeyValuePair<string, object> kv in root)
                        if (string.Equals(kv.Key, "book_sources", StringComparison.OrdinalIgnoreCase))
                            arr = kv.Value as System.Collections.IEnumerable;
                }
            }
            else arr = rootObj as System.Collections.IEnumerable;

            if (arr == null)
            {
                LastIssues.Add("文件里没有找到书源数组（应有 book_sources 字段）");
                return list;
            }

            int index = 0;
            foreach (object o in arr)
            {
                index++;
                Dictionary<string, object> m = o as Dictionary<string, object>;
                if (m == null) { LastIssues.Add("第 " + index + " 条不是对象"); continue; }

                SourceDef sd = new SourceDef();
                sd.Name = Pick(m, "title", "name", "source_name", "book_source_name");
                sd.Host = Pick(m, "host", "base_url", "home", "url_home");
                sd.QueryUrl = Pick(m, "query_url", "search_url", "searchUrl", "url");
                sd.QueryCharset = PickInt(m, "query_charset", "charset", "queryCharset");
                sd.BookNameXPath = Pick(m, "book_name_xpath", "bookNameXPath", "name_xpath");
                sd.BookHrefXPath = Pick(m, "book_mainpage_xpath", "book_url_xpath", "bookUrlXPath", "mainpage_xpath");
                sd.BookAuthorXPath = Pick(m, "book_author_xpath", "author_xpath");
                sd.ChapterTitleXPath = Pick(m, "chapter_title_xpath", "chapter_name_xpath", "chapterTitleXPath");
                sd.ChapterUrlXPath = Pick(m, "chapter_url_xpath", "chapterUrlXPath");
                sd.ContentXPath = Pick(m, "content_xpath", "content", "content_rule", "contentXPath");
                sd.QueryMethod = PickInt(m, "query_method", "method");
                sd.Charset = sd.QueryCharset == 2 ? "gbk" : "utf-8";
                sd.Id = Store.MakeId(sd.Name + sd.Host);
                sd.Enabled = true;

                if (string.IsNullOrEmpty(sd.Name)) sd.Name = "书源 " + index;
                if (string.IsNullOrEmpty(sd.Host) && !string.IsNullOrEmpty(sd.QueryUrl))
                {
                    try { Uri u = new Uri(sd.QueryUrl.Replace("%s", "x")); sd.Host = u.Scheme + "://" + u.Host; }
                    catch { }
                }
                if (string.IsNullOrEmpty(sd.ContentXPath))
                {
                    LastIssues.Add("第 " + index + " 条（" + sd.Name + "）缺少 content_xpath");
                    continue;
                }
                list.Add(sd);
            }
            if (list.Count == 0 && LastIssues.Count == 0) LastIssues.Add("书源数组是空的");
            return list;
        }

        static string Pick(Dictionary<string, object> m, params string[] keys)
        {
            foreach (string k in keys)
            {
                object v;
                if (m.TryGetValue(k, out v) && v != null)
                {
                    string str = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(str)) return str;
                }
            }
            foreach (KeyValuePair<string, object> kv in m)
                foreach (string k in keys)
                    if (string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                    {
                        string str = Convert.ToString(kv.Value);
                        if (!string.IsNullOrEmpty(str)) return str;
                    }
            return "";
        }

        static int PickInt(Dictionary<string, object> m, params string[] keys)
        {
            string str = Pick(m, keys);
            int v;
            return int.TryParse(str, out v) ? v : 0;
        }

        // ------------------------------------------------------------ HTTP
        public static string Get(string url, string charset) { return Get(url, charset, null); }

        public static string Get(string url, string charset, string referer)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Lamplight/0.2";
                if (!string.IsNullOrEmpty(referer)) { try { req.Referer = referer; } catch { } }
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;
                req.AllowAutoRedirect = true;
                req.KeepAlive = false;
                string proxy = Store.Lib.Settings.Proxy;
                if (!string.IsNullOrEmpty(proxy))
                {
                    try { req.Proxy = new WebProxy(proxy); } catch { }
                }
                else req.Proxy = null;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (Stream st = res.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[16384];
                    int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return Decode(ms.ToArray(), charset);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("网络请求失败：" + ex.Message);
            }
        }

        static string Decode(byte[] data, string charset)
        {
            if (string.Equals(charset, "gbk", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "gb2312", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(charset, "gb18030", StringComparison.OrdinalIgnoreCase))
            {
                try { return Encoding.GetEncoding(54936).GetString(data); } catch { }
            }
            string s;
            try { s = new UTF8Encoding(false, true).GetString(data); }
            catch { try { s = Encoding.GetEncoding(54936).GetString(data); } catch { s = Encoding.UTF8.GetString(data); } }

            Match m = Regex.Match(s, @"charset\s*=\s*[""']?([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
            if (m.Success && !charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string enc = m.Groups[1].Value;
                    if (enc.StartsWith("gb", StringComparison.OrdinalIgnoreCase))
                        return Encoding.GetEncoding(54936).GetString(data);
                }
                catch { }
            }
            return s;
        }

        static string Abs(string host, string href)
        {
            if (string.IsNullOrEmpty(href)) return href;
            href = href.Trim();
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return href;
            try
            {
                Uri b = new Uri(host.EndsWith("/") ? host : host + "/");
                return new Uri(b, href).ToString();
            }
            catch { return href; }
        }

        static string Clean(string html)
        {
            if (html == null) return "";
            string s = Regex.Replace(html, @"(?is)<script.*?</script>", "");
            s = Regex.Replace(s, @"(?is)<style.*?</style>", "");
            s = Regex.Replace(s, @"(?i)<br\s*/?>", "\n");
            s = Regex.Replace(s, @"(?i)</p\s*>", "\n\n");
            s = Regex.Replace(s, @"(?is)<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            s = s.Replace("\r", "");
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        // ------------------------------------------------------------ 搜索
        static Encoding ReadEncoding(string charset)
        {
            try
            {
                if (string.IsNullOrEmpty(charset)) return Encoding.UTF8;
                if (charset.Equals("gbk", StringComparison.OrdinalIgnoreCase) ||
                    charset.Equals("gb2312", StringComparison.OrdinalIgnoreCase)) return Encoding.GetEncoding(936);
                return Encoding.GetEncoding(charset);
            }
            catch { return Encoding.UTF8; }
        }

        public static string DecodeBytes(byte[] data, string charset, string contentType)
        {
            if (data == null || data.Length == 0) return "";
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);

            string name = charset;
            if (string.IsNullOrEmpty(name) || name.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            {
                int scan = Math.Min(data.Length, 4096);
                string head = Encoding.ASCII.GetString(data, 0, scan);
                Match mm = Regex.Match(head, @"charset\s*=\s*[""]?([\w-]+)", RegexOptions.IgnoreCase);
                if (!mm.Success) mm = Regex.Match(head, @"charset=([\w-]+)", RegexOptions.IgnoreCase);
                if (mm.Success) name = mm.Groups[1].Value;
                else if (!string.IsNullOrEmpty(contentType))
                {
                    Match mc = Regex.Match(contentType, @"charset\s*=\s*([\w-]+)", RegexOptions.IgnoreCase);
                    if (mc.Success) name = mc.Groups[1].Value;
                }
            }
            try
            {
                Encoding enc = ReadEncoding(name);
                if (enc.CodePage == 65001)
                {
                    try { return new UTF8Encoding(false, true).GetString(data); }
                    catch { return Encoding.GetEncoding(936).GetString(data); }
                }
                return enc.GetString(data);
            }
            catch { return Encoding.UTF8.GetString(data); }
        }

        static List<string> ValuesAny(HNode root, string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return new List<string>();
            foreach (string part in xpath.Split(new string[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
            {
                List<string> v = HtmlLite.Values(root, part.Trim());
                if (v.Count > 0) return v;
            }
            return new List<string>();
        }

        static List<HNode> SelectAny(HNode root, string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return new List<HNode>();
            foreach (string part in xpath.Split(new string[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
            {
                List<HNode> v = HtmlLite.Select(root, part.Trim());
                if (v.Count > 0) return v;
            }
            return new List<HNode>();
        }

        public static string Post(string url, string body, string charset)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Lamplight/0.2";
            req.Timeout = 12000;
            req.ContentType = "application/x-www-form-urlencoded";
            byte[] data = Encoding.UTF8.GetBytes(body ?? "");
            req.ContentLength = data.Length;
            using (Stream st = req.GetRequestStream()) st.Write(data, 0, data.Length);
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream rs = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(rs, ReadEncoding(charset)))
                return reader.ReadToEnd();
        }

        public static List<OnlineBook> Search(SourceDef src, string query, out string error)
        {
            error = null;
            List<OnlineBook> list = new List<OnlineBook>();
            try
            {
                string url = src.IsXPath ? src.QueryUrl : src.SearchUrl;
                url = url.Replace("{{key}}", "{q}").Replace("{{page}}", "{page}");
                url = url.Replace("{q}", Uri.EscapeDataString(query)).Replace("%s", Uri.EscapeDataString(query)).Replace("{page}", "1");
                url = Abs(src.Host, url);
                string html;
                if (src.IsXPath && src.QueryMethod == 1 && !string.IsNullOrEmpty(src.QueryBody))
                {
                    string body = src.QueryBody.Replace("{{key}}", Uri.EscapeDataString(query))
                                               .Replace("{q}", Uri.EscapeDataString(query))
                                               .Replace("{{page}}", "1");
                    html = Post(url, body, src.Charset ?? "utf-8");
                }
                else html = Get(url, src.Charset ?? "utf-8");
                if (src.IsXPath)
                {
                    HNode root = HtmlLite.Parse(html);
                    List<string> names = ValuesAny(root, src.BookNameXPath);
                    List<string> hrefs = ValuesAny(root, src.BookHrefXPath);
                    List<string> authors = ValuesAny(root, src.BookAuthorXPath);
                    int cnt = Math.Min(names.Count, hrefs.Count);
                    for (int i = 0; i < cnt && list.Count < 30; i++)
                    {
                        if (string.IsNullOrEmpty(names[i]) || string.IsNullOrEmpty(hrefs[i])) continue;
                        list.Add(new OnlineBook
                        {
                            Title = names[i],
                            Author = i < authors.Count ? authors[i] : "",
                            Url = Abs(url, hrefs[i]),
                            SourceId = src.Id,
                            SourceName = src.Name
                        });
                    }
                    return list;
                }
                Regex rx = new Regex(src.SearchItemRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in rx.Matches(html))
                {
                    string title = m.Groups["title"].Success ? Clean(m.Groups["title"].Value) : "";
                    string href = m.Groups["url"].Success ? m.Groups["url"].Value : "";
                    string author = m.Groups["author"].Success ? Clean(m.Groups["author"].Value) : "";
                    if (title.Length == 0 || href.Length == 0) continue;
                    list.Add(new OnlineBook
                    {
                        Title = title,
                        Author = author,
                        Url = Abs(url, href),
                        SourceId = src.Id,
                        SourceName = src.Name
                    });
                    if (list.Count >= 30) break;
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return list;
        }

        // ------------------------------------------------------------ 目录
        public static List<string[]> GetToc(SourceDef src, string bookUrl, out string error)
        {
            error = null;
            List<string[]> list = new List<string[]>();
            try
            {
                string html = Get(bookUrl, src.Charset ?? "utf-8", bookUrl);
                if (src.IsXPath)
                {
                    HNode root = HtmlLite.Parse(html);
                    List<string> titles = ValuesAny(root, src.ChapterTitleXPath);
                    List<string> urls = ValuesAny(root, src.ChapterUrlXPath);
                    int cnt2 = Math.Min(titles.Count, urls.Count);
                    for (int i = 0; i < cnt2; i++)
                        list.Add(new string[] { titles[i], Abs(bookUrl, urls[i]) });
                    return list;
                }
                Regex rx = new Regex(src.ChapterListRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in rx.Matches(html))
                {
                    string title = m.Groups["title"].Success ? Clean(m.Groups["title"].Value) : "";
                    string href = m.Groups["url"].Success ? m.Groups["url"].Value : "";
                    if (title.Length == 0 || href.Length == 0) continue;
                    list.Add(new string[] { title, Abs(bookUrl, href) });
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return list;
        }

        // ------------------------------------------------------------ 正文
        public static string TocFile(string bookId) { return Path.Combine(CacheDir(bookId), "toc.json"); }

        public static string CacheDir(string bookId)
        {
            string d = Path.Combine(Store.DataDir, "cache", bookId);
            Directory.CreateDirectory(d);
            return d;
        }

        public static void SaveToc(string bookId, List<string[]> toc)
        {
            try { File.WriteAllText(TocFile(bookId), Json.Serialize(toc), new UTF8Encoding(false)); } catch { }
        }

        public static List<string[]> LoadToc(string bookId)
        {
            try
            {
                if (!File.Exists(TocFile(bookId))) return null;
                return Json.Deserialize<List<string[]>>(File.ReadAllText(TocFile(bookId), Encoding.UTF8));
            }
            catch { return null; }
        }

        public static string GetChapter(BookEntry book, int index, out string error)
        {
            error = null;
            string cache = Path.Combine(CacheDir(book.Id), "c" + index + ".txt");
            if (File.Exists(cache))
            {
                try { return File.ReadAllText(cache, Encoding.UTF8); } catch { }
            }
            SourceDef src = Find(book.SourceId);
            if (src == null) { error = "书源不存在"; return ""; }
            List<string[]> toc = LoadToc(book.Id);
            if (toc == null || index < 0 || index >= toc.Count) { error = "目录缺失"; return ""; }
            try
            {
                string html = Get(toc[index][1], src.Charset ?? "utf-8", book.Url);
                string text;
                if (src.IsXPath)
                {
                    HNode root = HtmlLite.Parse(html);
                    List<HNode> nodes = SelectAny(root, src.ContentXPath);
                    text = nodes.Count > 0 ? nodes[0].InnerText() : "";
                    text = Regex.Replace(text, @"[ \t]+", " ");
                    text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
                }
                else
                {
                    Regex rx = new Regex(src.ContentRegex, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    Match m = rx.Match(html);
                    text = m.Success && m.Groups["content"].Success ? Clean(m.Groups["content"].Value) : Clean(html);
                }
                if (string.IsNullOrEmpty(text)) { error = "正文为空"; return ""; }
                File.WriteAllText(cache, text, new UTF8Encoding(false));
                return text;
            }
            catch (Exception ex) { error = ex.Message; return ""; }
        }
    }
}
