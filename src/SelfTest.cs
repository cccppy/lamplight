using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;

namespace Lamplight
{
    /// <summary>无界面自检：编码、分章、排版、存取与性能。--selftest 运行。</summary>
    static class SelfTest
    {
        static int _pass, _fail;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  (" + detail + ")" : "")); }
            else { _fail++; Console.WriteLine("FAIL  " + name + "  " + detail); }
        }

        public static int Run()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lamplight-selftest");
            Directory.CreateDirectory(dir);

            // 1. 编码
            string sample = "第一章 测试\n\n　　这是一段中文测试文本，包含 English words 和 123 数字。\n";
            string p1 = Path.Combine(dir, "utf8.txt");
            string p2 = Path.Combine(dir, "gbk.txt");
            string p3 = Path.Combine(dir, "utf16.txt");
            File.WriteAllText(p1, sample, new UTF8Encoding(false));
            File.WriteAllText(p2, sample, Encoding.GetEncoding(936));
            File.WriteAllText(p3, sample, Encoding.Unicode);

            string cs;
            string t1 = TextDoc.Decode(File.ReadAllBytes(p1), out cs);
            Check("UTF-8 解码", t1 == sample && cs == "UTF-8", cs);
            string t2 = TextDoc.Decode(File.ReadAllBytes(p2), out cs);
            Check("GBK 解码", t2 == sample, cs);
            string t3 = TextDoc.Decode(File.ReadAllBytes(p3), out cs);
            Check("UTF-16 解码", t3.Replace("\r\n", "\n") == sample.Replace("\r\n", "\n"), cs);

            // 2. 分章
            StringBuilder sb = new StringBuilder();
            for (int i = 1; i <= 12; i++)
            {
                sb.AppendLine("第" + i + "章 标题" + i);
                sb.AppendLine();
                for (int p = 0; p < 6; p++)
                    sb.AppendLine("　　这是第" + i + "章的第" + p + "段，用来测试分章与排版。夜色像一层薄薄的墨，慢慢洇开在窗棂上。");
                sb.AppendLine();
            }
            string book = sb.ToString();
            List<Chapter> chs = TextDoc.Split(book);
            Check("分章数量", chs.Count == 12, chs.Count + " 章");
            Check("首章标题", chs.Count > 0 && chs[0].Title.Contains("第1章"), chs.Count > 0 ? chs[0].Title : "-");

            // 3. 排版
            Font f = new Font("Microsoft YaHei UI", 20f, FontStyle.Regular, GraphicsUnit.Pixel);
            long t0 = Environment.TickCount;
            string body = book.Substring(chs[0].Start, chs[0].Length);
            DocLayout lay = TextDoc.Build(body, 740f, f, 38f, 16f, 40f);
            long buildMs = Environment.TickCount - t0;
            Check("排版行数", lay.Lines.Count > 0, lay.Lines.Count + " 行, " + buildMs + " ms");
            bool indentOk = lay.Lines.Count > 1 && lay.Lines[0].ParaStart && lay.Lines[0].Len > 0;
            Check("首行标记", indentOk, "");

            int total = 0;
            foreach (LineInfo ln in lay.Lines) total += ln.Len;
            Check("行覆盖正文", total > body.Length * 0.80, total + " / " + body.Length);

            // 4. 存取
            Library lib = new Library();
            BookEntry b = new BookEntry();
            b.Id = "test01"; b.Path = p1; b.AutoName = "测试书"; b.Name = "自定义书名";
            b.Bookmarks.Add(new BookmarkItem { Chapter = 0, Offset = 10, ChapterTitle = "第一章 测试", Excerpt = "这是一段" });
            b.Excerpts.Add(new ExcerptItem { Chapter = 0, Start = 10, Length = 5, ChapterTitle = "第一章 测试", Text = "这是一段" });
            lib.Books.Add(b);
            string jsonFile = Path.Combine(dir, "lib.json");
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(lib);
            File.WriteAllText(jsonFile, json, new UTF8Encoding(false));
            Library back = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Library>(File.ReadAllText(jsonFile));
            Check("存取书签", back.Books.Count == 1 && back.Books[0].Bookmarks.Count == 1, "");
            Check("存取摘录", back.Books[0].Excerpts.Count == 1 && back.Books[0].Excerpts[0].Text == "这是一段", "");
            Check("自定义书名", back.Books[0].DisplayName == "自定义书名", back.Books[0].DisplayName);

            // 5. 50MB 级性能（约 20 MB 文本）
            StringBuilder big = new StringBuilder();
            while (big.Length < 20 * 1024 * 1024) big.Append(book);
            string bigText = big.ToString();
            byte[] bigBytes = Encoding.UTF8.GetBytes(bigText);
            t0 = Environment.TickCount;
            string dt;
            string decoded = TextDoc.Decode(bigBytes, out dt);
            long decMs = Environment.TickCount - t0;
            t0 = Environment.TickCount;
            List<Chapter> bigChs = TextDoc.Split(decoded);
            long splitMs = Environment.TickCount - t0;
            Check("20MB 解码+分章", decMs + splitMs < 3000,
                bigBytes.Length / 1024 / 1024 + " MB, 解码 " + decMs + " ms, 分章 " + splitMs + " ms, " + bigChs.Count + " 章");

            // 6. 字体探测
            Check("正文衬线字体", new Font(Painter.SerifFamily, 20f).FontFamily.Name.Length > 0, Painter.SerifFamily);

            Console.WriteLine();
            Console.WriteLine("RESULT  pass=" + _pass + "  fail=" + _fail);
            return _fail == 0 ? 0 : 1;
        }
    }
}
