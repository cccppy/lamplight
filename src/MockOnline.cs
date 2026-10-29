using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Lamplight
{
    /// <summary>本地假书源：用于离线验证搜索/目录/正文整条链路。</summary>
    static class MockOnline
    {
        public static int Port = 18765;

        public static void Start()
        {
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
            Thread.Sleep(150);
        }

        static void Loop()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, Port);
            try { listener.Start(); }
            catch { return; }
            while (true)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate(object o) { Serve((TcpClient)o); }, client);
                }
                catch { return; }
            }
        }

        static void Serve(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    byte[] buf = new byte[4096];
                    int n = ns.Read(buf, 0, buf.Length);
                    string req = Encoding.ASCII.GetString(buf, 0, n);
                    string path = "/";
                    int sp1 = req.IndexOf(' ');
                    int sp2 = req.IndexOf(' ', sp1 + 1);
                    if (sp1 > 0 && sp2 > sp1) path = req.Substring(sp1 + 1, sp2 - sp1 - 1);
                    string body = Route(path);
                    byte[] html = Encoding.UTF8.GetBytes(body);
                    string head = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " +
                                  html.Length + "\r\nConnection: close\r\n\r\n";
                    byte[] hb = Encoding.ASCII.GetBytes(head);
                    ns.Write(hb, 0, hb.Length);
                    ns.Write(html, 0, html.Length);
                }
            }
            catch { }
        }

        static string Route(string path)
        {
            if (path.StartsWith("/search"))
                return "<html><body><div class=\"result\"><h4 class=\"bookname\"><a href=\"/book/1\">长夜将明</a></h4>" +
                       "<div class=\"author\">沈砚</div></div><div class=\"result\"><h4 class=\"bookname\"><a href=\"/book/2\">山月记</a></h4>" +
                       "<div class=\"author\">佚名</div></div></body></html>";
            if (path == "/book/1")
                return "<html><body><div class=\"listmain\"><dl>" +
                       "<dd><a href=\"/c/1\">第一章 更鼓</a></dd><dd><a href=\"/c/2\">第二章 夜行</a></dd>" +
                       "</dl></div></body></html>";
            if (path == "/c/1")
                return "<html><body><div id=\"content\">夜色像一层薄薄的墨。<br><br>他坐在桌前，把那封信翻来覆去地看。</div></body></html>";
            if (path == "/c/2")
                return "<html><body><div id=\"content\">门外忽然有脚步声。<br><br>他没有应声。</div></body></html>";
            return "<html><body>ok</body></html>";
        }

        public static string BsJson()
        {
            return "{\"book_sources\":[{\"title\":\"本地测试源\",\"host\":\"http://127.0.0.1:" + Port + "\"," +
                   "\"query_url\":\"http://127.0.0.1:" + Port + "/search?q=%s\",\"query_method\":0,\"query_charset\":1," +
                   "\"book_name_xpath\":\"//h4[@class='bookname']/a\"," +
                   "\"book_mainpage_xpath\":\"//h4[@class='bookname']/a/@href\"," +
                   "\"book_author_xpath\":\"//div[@class='author']\"," +
                   "\"chapter_title_xpath\":\"//div[@class='listmain']/dl/dd/a\"," +
                   "\"chapter_url_xpath\":\"//div[@class='listmain']/dl/dd/a/@href\"," +
                   "\"content_xpath\":\"//*[@id=\\\"content\\\"]\"}]}";
        }
    }
}
