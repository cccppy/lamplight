using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Lamplight
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            Store.Init(exeDir.TrimEnd('\\'));
            Online.Init(Store.DataDir);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs te)
            {
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(Store.DataDir, "crash.txt"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [ui]  " + te.Exception + "\r\n\r\n",
                        Encoding.UTF8);
                }
                catch { }
            };

            // 预览模式：灯下.exe --render out.png shelf|reader|bookmarks|toc|excerpts|settings [sample.txt]
            if (args.Length >= 2 && args[0] == "--split-test")
            {
                string txt = File.ReadAllText(args[1], Encoding.UTF8);
                List<int[]> sp = Tts.Split(txt);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("len=" + txt.Length + " spans=" + sp.Count);
                foreach (int[] a in sp)
                {
                    int st = Math.Max(0, Math.Min(txt.Length, a[0]));
                    int ln = Math.Max(0, Math.Min(txt.Length - st, a[1]));
                    sb.AppendLine(a[0] + "," + a[1] + " [" + txt.Substring(st, ln).Replace("\r", "\\r").Replace("\n", "\\n") + "]");
                }
                File.WriteAllText(Path.Combine(Store.DataDir, "split-test.txt"), sb.ToString(), Encoding.UTF8);
                return;
            }

            if (args.Length >= 2 && args[0] == "--legado-test")
            {
                string json = File.ReadAllText(args[1], Encoding.UTF8);
                List<string> issues = new List<string>();
                List<SourceDef> conv = Legado.Convert(json, issues);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("file=" + args[1]);
                sb.AppendLine("converted=" + conv.Count + " skipped=" + issues.Count);
                foreach (string it in issues) sb.AppendLine("issue=" + it);
                File.WriteAllText(Path.Combine(Store.DataDir, "legado-test.txt"), sb.ToString(), Encoding.UTF8);
                return;
            }

            if (args.Length >= 1 && args[0] == "--uicheck")
            {
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lamplight-uicheck");
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                Directory.CreateDirectory(tmp);
                Store.Init(tmp);
                Online.Init(Store.DataDir);
                Store.Lib = new Library();
                SeedDemo();
                Environment.ExitCode = UiCheck.Run();
                return;
            }

            if (args.Length >= 1 && args[0] == "--tts-test")
            {
                string report;
                byte[] wav;
                bool ok = Tts.SynthChain("这是灯下的朗读自检。", 0, 90, "", out wav, out report);
                string path = Path.Combine(Store.DataDir, "tts-report.txt");
                StringBuilder rb = new StringBuilder();
                rb.AppendLine("available=" + Tts.Available);
                rb.AppendLine("synth_ok=" + ok);
                rb.AppendLine("bytes=" + (wav == null ? 0 : wav.Length));
                rb.AppendLine("chain=" + report);
                rb.AppendLine("voices=" + string.Join(" | ", Tts.Voices()));
                try
                {
                    File.WriteAllText(path, rb.ToString(), Encoding.UTF8);
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                }
                catch { }
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }

            if (args.Length >= 1 && args[0] == "--online-test")
            {
                string tmp0 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lamplight-onlinetest");
                try { if (Directory.Exists(tmp0)) Directory.Delete(tmp0, true); } catch { }
                Directory.CreateDirectory(tmp0);
                Store.Init(tmp0);
                Online.Init(Store.DataDir);
                Environment.ExitCode = OnlineTest.Run();
                return;
            }

            if (args.Length >= 1 && args[0] == "--selftest")
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (args.Length >= 2 && args[0] == "--smoke")
            {
                Smoke(args[1]);
                return;
            }

            if (args.Length >= 3 && args[0] == "--render")
            {
                RenderPreview(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            MainForm f = new MainForm();
            if (args.Length > 0 && File.Exists(args[0]))
            {
                BookEntry b = Store.AddOrGet(args[0]);
                f.Shown += delegate { f.OpenBook(b, true); };
            }
            Application.Run(f);
        }

        static void Smoke(string outPath)
        {
            Application.EnableVisualStyles();
            MainForm f = new MainForm();
            f.CreateControl();
            f.Scale = 1f;
            f.ClientSize = new Size(1200, 780);
            System.Reflection.MethodInfo mi = typeof(MainForm).GetMethod("OnLoad",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            mi.Invoke(f, new object[] { EventArgs.Empty });
            f.PerformLayout();
            Application.DoEvents();

            using (Bitmap bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(outPath, ImageFormat.Png);
            }
            foreach (Control c in f.Controls)
            {
                TextBox tb = c as TextBox;
                if (tb != null)
                    Console.WriteLine("TB " + tb.Name + " visible=" + tb.Visible + " bounds=" + tb.Bounds + " focused=" + tb.Focused + " text=" + tb.Text);
            }
            Console.WriteLine("smoke ok, books=" + Store.Lib.Books.Count + " theme=" + Store.Lib.Settings.Theme);
        }

        static void RenderPreview(string[] args)
        {
            string outPath = args[1];
            string mode = args[2];
            string sample = null;
            if (args.Length >= 4 && File.Exists(args[3]))
                sample = File.ReadAllText(args[3], Encoding.UTF8);
            else
                sample = DemoText();

            if (Store.Lib.Books.Count == 0) SeedDemo();

            MainForm f = new MainForm();
            f.PreviewMode = true;
            f.CreateControl();
            f.Scale = 1f;
            f.ClientSize = new Size(1200, 780);
            bool shelfMode = mode == "shelf" || mode == "rename";
            f.EnterPreview(mode, shelfMode ? null : sample);
            if (shelfMode) { f.Hover = 2; f.Selected = 0; }
            if (mode == "rename") { f.Selected = 1; f.PreviewRename = 1; }
            f.PerformLayout();
            Application.DoEvents();


            using (Bitmap bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(outPath, ImageFormat.Png);
            }
            Console.WriteLine("ok " + outPath);
        }

        static void SeedDemo()
        {
            string[] names = { "长夜将明", "山月记", "北方的河", "雾中列车", "旧城手记", "呼兰河传", "边城", "无人生还" };
            long now = Store.Now();
            string demoDir = Path.Combine(Store.DataDir, "示例书籍");
            Directory.CreateDirectory(demoDir);
            double[] pcts = { 0.432, 0.781, 0.0, 0.124, 1.0, 0.36, 0.05, 0.0 };
            for (int i = 0; i < names.Length; i++)
            {
                BookEntry b = new BookEntry();
                b.Path = Path.Combine(demoDir, names[i] + ".txt");
                b.Id = Store.MakeId(b.Path);
                b.AutoName = names[i];
                b.Added = now - (long)(i * 86400 * 2);
                b.LastRead = i == 0 ? now - 3600 : (i == 1 ? now - 90000 : now - i * 86400 * 3);
                b.Progress = new Progress { Chapter = 2, Offset = 0, Percent = pcts[i] };
                if (!File.Exists(b.Path))
                    File.WriteAllText(b.Path, DemoText(), new UTF8Encoding(false));
                if (i == 6) File.Delete(b.Path); // 故意留一条缺失，展示异常态

                if (i == 0)
                {
                    string text = File.ReadAllText(b.Path);
                    List<Chapter> chs = TextDoc.Split(text);
                    b.Bookmarks.Add(MakeBookmark(chs, text, 1, 40, now - 7200));
                    b.Bookmarks.Add(MakeBookmark(chs, text, 2, 180, now - 3600));
                    b.Bookmarks.Add(MakeBookmark(chs, text, 2, 620, now - 3500));
                    b.ReadLog.Add(new ChapterRead { Chapter = 0, Percent = 1f });
                    b.ReadLog.Add(new ChapterRead { Chapter = 1, Percent = 0.42f });
                    b.ReadingSeconds = 5400;
                    b.Progress.Chapter = 2;
                    b.Progress.Offset = chs.Count > 2 ? chs[2].Start + 180 : 0;
                }
                Store.Lib.Books.Add(b);
            }
        }

        static BookmarkItem MakeBookmark(List<Chapter> chs, string text, int ch, int rel, long when)
        {
            if (chs.Count == 0) chs.Add(new Chapter { Title = "正文", Start = 0, Length = text.Length });
            ch = Math.Min(ch, chs.Count - 1);
            int abs = chs[ch].Start + Math.Min(rel, Math.Max(0, chs[ch].Length - 1));
            int len = Math.Min(42, Math.Max(0, text.Length - abs));
            string ex = text.Substring(abs, len).Replace("\r", "").Replace("\n", " ").Trim();
            return new BookmarkItem { Chapter = ch, Offset = abs, ChapterTitle = chs[ch].Title, Excerpt = ex, Created = when };
        }

        static ExcerptItem MakeExcerpt(List<Chapter> chs, string text, int ch, int rel, int len, long when)
        {
            if (chs.Count == 0) chs.Add(new Chapter { Title = "正文", Start = 0, Length = text.Length });
            ch = Math.Min(ch, chs.Count - 1);
            int abs = chs[ch].Start + Math.Min(rel, Math.Max(0, chs[ch].Length - 1));
            len = Math.Min(len, Math.Max(0, text.Length - abs));
            string ex = text.Substring(abs, len).Replace("\r", "").Replace("\n", " ").Trim();
            return new ExcerptItem { Chapter = ch, Start = abs, Length = len, ChapterTitle = chs[ch].Title, Text = ex, Created = when };
        }

        static string DemoText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("第一章 更鼓");
            sb.AppendLine();
            sb.AppendLine("　　夜色像一层薄薄的墨，慢慢洇开在窗棂上。远处的更鼓声一下一下，敲得人心也跟着空了一拍。他坐在桌前，把那封没有落款的信翻来覆去地看，纸边已经被摩挲得起了毛。");
            sb.AppendLine("　　门外忽然有脚步声，不急，也不缓，像是早就知道屋里有人醒着。他伸手按住灯罩，把那点昏黄的光压得更低了些，指节抵着木沿，能听见自己心跳撞在掌心的声音。");
            sb.AppendLine("　　“沈先生睡了吗？”声音隔着门板传进来，客气得过分，尾音却微微上扬，像在笑，又像在试探。");
            sb.AppendLine();
            sb.AppendLine("第二章 夜行");
            sb.AppendLine();
            sb.AppendLine("　　他没有应声，只把信折好，塞进衣袖最里层的夹袋。桌上的茶早就凉透了，浮着一层细白的沫。他端起来，一口饮尽，苦得清醒。");
            sb.AppendLine("　　门闩被轻轻叩了三下。一下，两下，第三下却迟迟没有落下来。窗外的风忽然停了，连那点更鼓的余音也散得干净。");
            sb.AppendLine();
            sb.AppendLine("第五章 不速之客");
            sb.AppendLine();
            for (int p = 0; p < 9; p++)
            {
                sb.AppendLine("　　雨是后半夜下起来的，先是很轻，落在瓦上有一种细碎的沙沙声。他披衣起身，把窗推开一条缝，潮气混着泥土的味道涌进来。街对面那盏灯还亮着，灯下站着一个人，撑一把黑伞，一动不动。");
                sb.AppendLine("　　他认得那把伞。三年前在渡口，也是这样的雨夜，撑伞的人说了一句话，然后转身走进雨里，再没有回来。");
            }
            sb.AppendLine("　　他忽然想起信上的那句话：你若还活着，就不要来找我。");
            sb.AppendLine();
            sb.AppendLine("第六章 尾声");
            sb.AppendLine();
            sb.AppendLine("　　天光终于亮透了，巷子里的水汽一点点散开。他把那封信重新叠好，放进最里层的口袋，转身往渡口走。");
            return sb.ToString();
        }
    }
}
