using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Lamplight
{
    static class OnlineTest
    {
        static int _pass, _fail;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  (" + detail + ")" : "")); }
            else { _fail++; Console.WriteLine("FAIL  " + name + "  " + detail); }
        }

        public static int Run()
        {
            MockOnline.Start();

            // 1) HTML + XPath 子集
            string html = "<div class='listmain'><dl><dd><a href='/c/1'>第一章</a></dd><dd><a href='/c/2'>第二章</a></dd></dl></div>" +
                          "<div id=\"content\">正文<br>第二行</div>";
            HNode root = HtmlLite.Parse(html);
            List<string> titles = HtmlLite.Values(root, "//div[@class='listmain']/dl/dd/a");
            List<string> hrefs = HtmlLite.Values(root, "//div[@class='listmain']/dl/dd/a/@href");
            Check("XPath 取文本", titles.Count == 2 && titles[0] == "第一章", string.Join(",", titles.ToArray()));
            Check("XPath 取属性", hrefs.Count == 2 && hrefs[1] == "/c/2", string.Join(",", hrefs.ToArray()));
            List<HNode> content = HtmlLite.Select(root, "//*[@id=\"content\"]");
            Check("XPath 通配 + id", content.Count == 1 && content[0].InnerText().Contains("第二行"), content.Count > 0 ? content[0].InnerText() : "-");

            // 2) bs.json 解析
            string bsjson = MockOnline.BsJson();
            List<SourceDef> list;
            try { list = Online.ParseBsJson(bsjson); }
            catch (Exception ex) { Console.WriteLine("PARSE ERR: " + ex.Message); list = new List<SourceDef>(); }
            List<SourceDef> arrRoot = Online.ParseBsJson("[{\"name\":\"A\",\"content_xpath\":\"//div\"}]");
            List<SourceDef> altKeys = Online.ParseBsJson("{\"sources\":[{\"title\":\"B\",\"content\":\"//p\",\"base_url\":\"http://b\"}]}");
            List<SourceDef> noRule = Online.ParseBsJson("{\"book_sources\":[{\"title\":\"C\"}]}");
            string legadoSample = "[{\"bookSourceName\":\"T\",\"bookSourceUrl\":\"http://x/\",\"searchUrl\":\"/s?q={{key}}\","
                + "\"ruleSearch\":{\"bookList\":\"#list li\",\"name\":\"a@text\",\"bookUrl\":\"a@href\"},"
                + "\"ruleToc\":{\"chapterList\":\"#toc a\",\"chapterName\":\"text\",\"chapterUrl\":\"href\"},"
                + "\"ruleContent\":{\"content\":\"#content\"}}]";
            List<string> lIssues = new List<string>();
            List<SourceDef> lConv = Legado.Convert(legadoSample, lIssues);
            Check("Legado 转换", lConv.Count == 1 && lConv[0].QueryUrl.Contains("{{key}}") && lConv[0].ContentXPath != null,
                lConv.Count + " 个 / " + string.Join(";", lIssues.ToArray()));

            Check("书源兼容根数组", arrRoot.Count == 1, arrRoot.Count + " 条");
            Check("书源兼容别名字段", altKeys.Count == 1 && altKeys[0].Name == "B", altKeys.Count + " 条");
            Check("缺规则有提示", noRule.Count == 0 && Online.LastIssues.Count > 0, Online.LastIssues.Count + " 条原因");

            Check("bs.json 解析", list.Count == 1 && list[0].IsXPath && list[0].Name == "本地测试源",
                list.Count > 0 ? list[0].Name + " / " + list[0].Host : "0");

            if (list.Count == 0) { Console.WriteLine("RESULT pass=" + _pass + " fail=" + _fail); return 1; }
            SourceDef src = list[0];
            Online.Sources = list;

            // 3) 搜索
            string err;
            List<OnlineBook> res = Online.Search(src, "长夜", out err);
            Check("书源搜索", err == null && res.Count == 2 && res[0].Title == "长夜将明",
                err ?? (res.Count + " 条 / " + (res.Count > 0 ? res[0].Title : "-")));

            if (res.Count == 0) { Console.WriteLine("RESULT pass=" + _pass + " fail=" + _fail); return 1; }

            // 4) 目录
            List<string[]> toc = Online.GetToc(src, res[0].Url, out err);
            Check("书源目录", err == null && toc.Count == 2 && toc[0][0] == "第一章 更鼓",
                err ?? (toc.Count + " 章"));

            // 5) 正文 + 缓存
            BookEntry b = new BookEntry();
            b.Id = "mockbook";
            b.Online = true;
            b.SourceId = src.Id;
            b.Url = res[0].Url;
            b.AutoName = res[0].Title;
            Online.SaveToc(b.Id, toc);
            string chap = Online.GetChapter(b, 0, out err);
            Check("正文抓取", err == null && chap.Contains("夜色"), err ?? chap.Substring(0, Math.Min(20, chap.Length)));
            string cache = Path.Combine(Online.CacheDir(b.Id), "c0.txt");
            HNode multi = HtmlLite.Parse("<div id=\"content\"><p>第一段</p><p>第二段</p></div>");
            List<string> parts = HtmlLite.Values(multi, "//*[@id=\"content\"]//p/text()");
            string joined = string.Join("|", parts.ToArray());
            Check("多段正文合并", parts.Count == 2 && joined.Contains("第一段") && joined.Contains("第二段"), joined);

            Check("正文缓存", File.Exists(cache), cache);
            string chap2 = Online.GetChapter(b, 1, out err);
            Check("第二章抓取", err == null && chap2.Contains("脚步声"), err ?? "");

            Console.WriteLine();
            Console.WriteLine("RESULT  pass=" + _pass + "  fail=" + _fail);
            return _fail == 0 ? 0 : 1;
        }
    }
}
