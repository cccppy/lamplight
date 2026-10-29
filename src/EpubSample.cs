using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Lamplight
{
    /// <summary>自检/演示用的最小 EPUB 生成器。</summary>
    static class EpubSample
    {
        public static void Build(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using (FileStream fs = File.Create(path))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Write(zip, "mimetype", "application/epub+zip");
                Write(zip, "META-INF/container.xml",
                    "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                    "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
                Write(zip, "OEBPS/content.opf",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\">" +
                    "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>示例电子书</dc:title><dc:language>zh</dc:language></metadata>" +
                    "<manifest><item id=\"c1\" href=\"c1.xhtml\" media-type=\"application/xhtml+xml\"/>" +
                    "<item id=\"c2\" href=\"c2.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>" +
                    "<spine><itemref idref=\"c1\"/><itemref idref=\"c2\"/></spine></package>");
                Write(zip, "OEBPS/c1.xhtml",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>第一章 起风</title></head>" +
                    "<body><h1>第一章 起风</h1><p>第一段测试正文，用来验证 EPUB 的解析与排版。</p><p>第二段：夜色像一层薄薄的墨。</p></body></html>");
                Write(zip, "OEBPS/c2.xhtml",
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>第二章 落雨</title></head>" +
                    "<body><h1>第二章 落雨</h1><p>第二章内容：门外忽然有脚步声。</p></body></html>");
            }
        }

        static void Write(ZipArchive zip, string name, string text)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            {
                byte[] b = Encoding.UTF8.GetBytes(text);
                s.Write(b, 0, b.Length);
            }
        }
    }
}
