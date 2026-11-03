using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Lamplight
{
    public partial class MainForm
    {
        public string LastImportReport = "";
        public int LastImportAdded;
        int LastImportParsed;
        int LastImportDup;
        bool LastImportLegado;

        public void ImportSources()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "书源文件 (*.json)|*.json|所有文件 (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string text;
                long bytes = 0;
                try
                {
                    byte[] data = File.ReadAllBytes(d.FileName);
                    bytes = data.Length;
                    string cs;
                    text = TextDoc.Decode(data, out cs);
                }
                catch (Exception ex) { ShowToast("读取失败：" + ex.Message); return; }
                ImportText(text, d.FileName, bytes, true);
            }
        }

        public void ImportSourcesFromUrl()
        {
            string url = PromptForm.Ask(this, "从网址导入书源",
                "粘贴书源 JSON 的网址（以 http:// 或 https:// 开头）", "", Th);
            if (url == null) return;
            url = url.Trim();
            if (url.Length == 0) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("网址需要以 http:// 或 https:// 开头");
                return;
            }

            string text;
            int bytes;
            Cursor prev = Cursor;
            Cursor = Cursors.WaitCursor;
            try
            {
                text = Online.Get(url, "utf-8");
                bytes = string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
            }
            catch (Exception ex)
            {
                Cursor = prev;
                MessageBox.Show(this,
                    "下载书源失败：\r\n\r\n" + ex.Message + "\r\n\r\n" +
                    "可以先用浏览器打开这个网址，另存为 .json 后再用「导入书源」导入。",
                    "从网址导入书源", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = prev;

            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                MessageBox.Show(this, "这个网址没有返回内容，或者返回的是空页面。",
                    "从网址导入书源", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ImportText(text, url, bytes, true);
        }

        // 供命令行测试使用：下载并导入，不弹窗
        public int ImportUrl(string url)
        {
            string text = Online.Get(url, "utf-8");
            ImportText(text, url, string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text), false);
            return LastImportAdded;
        }

        void ImportText(string text, string label, long bytes, bool interactive)
        {
            int added = 0, dup = 0, parsed = 0;
            StringBuilder diag = new StringBuilder();
            diag.AppendLine("source=" + label);
            diag.AppendLine("bytes=" + bytes);
            bool legado = text.IndexOf("bookSourceName", StringComparison.OrdinalIgnoreCase) >= 0
                       || text.IndexOf("ruleSearch", StringComparison.OrdinalIgnoreCase) >= 0
                       || text.IndexOf("ruleToc", StringComparison.OrdinalIgnoreCase) >= 0;
            diag.AppendLine("legado=" + legado);
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer();
                json.MaxJsonLength = int.MaxValue;
                if (legado)
                {
                    List<SourceDef> conv = Legado.Convert(text, Online.LastIssues);
                    parsed = conv.Count;
                    foreach (SourceDef s2 in conv)
                    {
                        if (Exists(s2)) { dup++; continue; }
                        Sources.Add(s2);
                        added++;
                    }
                }
                else if (text.IndexOf("book_sources", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.TrimStart().StartsWith("[") ||
                    text.IndexOf("\"sources\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    List<SourceDef> bs = Online.ParseBsJson(text);
                    parsed = bs.Count;
                    foreach (SourceDef s in bs)
                    {
                        if (Exists(s)) { dup++; continue; }
                        Sources.Add(s);
                        added++;
                    }
                }
                else
                {
                    List<SourceDef> list = json.Deserialize<List<SourceDef>>(text);
                    parsed = list == null ? 0 : list.Count;
                    if (list != null)
                        foreach (SourceDef s in list)
                        {
                            if (s == null || string.IsNullOrEmpty(s.Name)) continue;
                            if (s.Id == null) s.Id = Store.MakeId(s.Name + (s.Host ?? ""));
                            if (Exists(s)) { dup++; continue; }
                            Sources.Add(s);
                            added++;
                        }
                }
            }
            catch (Exception ex)
            {
                diag.AppendLine("parse_error=" + ex.Message);
                TryWriteDiag(diag);
                LastImportAdded = 0;
                LastImportParsed = 0;
                LastImportDup = 0;
                LastImportLegado = legado;
                LastImportReport = diag.ToString();
                if (interactive) ShowToast("解析失败：" + ex.Message);
                return;
            }

            try
            {
                diag.AppendLine("parsed=" + parsed + " added=" + added + " dup=" + dup);
                if (Online.LastIssues.Count > 0)
                    foreach (string it in Online.LastIssues) diag.AppendLine("issue=" + it);
                string keys = DescribeFirstEntry(text);
                if (keys.Length > 0) diag.AppendLine("first_keys=" + keys);
            }
            catch { }
            TryWriteDiag(diag);

            LastImportAdded = added;
            LastImportParsed = parsed;
            LastImportDup = dup;
            LastImportLegado = legado;
            LastImportReport = diag.ToString();

            if (added == 0 && interactive)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("没有导入任何书源。");
                sb.AppendLine();
                sb.AppendLine("来源：" + ShortLabel(label));
                sb.AppendLine("已解析到：" + parsed + " 条");
                sb.AppendLine("重复跳过：" + dup);
                if (Online.LastIssues.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("原因：");
                    for (int i = 0; i < Online.LastIssues.Count && i < 8; i++) sb.AppendLine("· " + Online.LastIssues[i]);
                }
                else if (parsed > 0) sb.AppendLine("· 这些书源已经导入过了");
                if (legado)
                {
                    sb.AppendLine();
                    sb.AppendLine("这是「阅读」格式的书源，已尝试转换；上面是没能转换的条目。");
                    sb.AppendLine("转换只支持常见 CSS 写法，含 JS / JSONPath 的规则无法使用。");
                }
                sb.AppendLine();
                sb.AppendLine("排查文件：" + Path.Combine(Store.DataDir, "sources-import.txt"));
                MessageBox.Show(this, sb.ToString(), "导入书源", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (added == 0) return;

            Online.Save();
            if (!interactive) return;
            SettingsSection = 3;
            ShowToast("已导入 " + added + " 个书源" + (dup > 0 ? "，重复 " + dup + " 个" : "") + (legado && Online.LastIssues.Count > 0 ? "，不支持 " + Online.LastIssues.Count + " 个" : ""));
            Invalidate();
        }

        static string ShortLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            if (label.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                label.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // 只显示文件名部分，避免 MessageBox 里一行过长
                int q = label.IndexOf('?');
                string s = q > 0 ? label.Substring(0, q) : label;
                int slash = s.LastIndexOf('/');
                return slash > 8 ? s.Substring(0, slash + 1) + "…" : s;
            }
            try { return Path.GetFileName(label); } catch { return label; }
        }

        void TryWriteDiag(StringBuilder sb)
        {
            try { File.WriteAllText(Path.Combine(Store.DataDir, "sources-import.txt"), sb.ToString(), Encoding.UTF8); }
            catch { }
        }

        static string DescribeFirstEntry(string text)
        {
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer();
                json.MaxJsonLength = int.MaxValue;
                object root = json.Deserialize<object>(text);
                List<string> keys = new List<string>();
                Dictionary<string, object> dict = root as Dictionary<string, object>;
                object arr = null;
                if (dict != null)
                {
                    foreach (string k in new string[] { "book_sources", "sources", "data", "list" })
                        if (dict.TryGetValue(k, out arr)) break;
                }
                if (arr == null) arr = root;
                System.Collections.IEnumerable en = arr as System.Collections.IEnumerable;
                if (en == null) return "root=" + (root == null ? "null" : root.GetType().Name);
                foreach (object o in en)
                {
                    Dictionary<string, object> m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    foreach (KeyValuePair<string, object> kv in m)
                    {
                        if (keys.Count >= 16) break;
                        keys.Add(kv.Key);
                    }
                    break;
                }
                return string.Join(",", keys.ToArray());
            }
            catch (Exception ex) { return "err:" + ex.Message; }
        }

        bool Exists(SourceDef s)
        {
            foreach (SourceDef x in Sources)
                if (string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Host, s.Host, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        int ImportBsJson(string text)
        {
            int added = 0;
            foreach (SourceDef s in Online.ParseBsJson(text))
            {
                if (Exists(s)) continue;
                Sources.Add(s);
                added++;
            }
            return added;
        }

        static string Str(Dictionary<string, object> m, string key)
        {
            object v;
            return m.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        static int Int(Dictionary<string, object> m, string key)
        {
            object v;
            if (!m.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }
    }
}
