using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Lamplight
{
    /// <summary>--tts-test：把朗读链路结果写成文件并弹窗，方便用户回传。</summary>
    static class TtsSelfTest
    {
        public static void Run()
        {
            string report = "";
            try
            {
                byte[] wav;
                string chain;
                bool ok = Tts.SynthChain("这是灯下的朗读自检。", 0, 90, "", out wav, out chain);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("time=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("exe=" + Application.ExecutablePath);
                sb.AppendLine("exe_time=" + SafeTime(Application.ExecutablePath));
                sb.AppendLine("is64=" + Environment.Is64BitProcess);
                sb.AppendLine("user=" + Environment.UserName);
                sb.AppendLine("session=" + System.Diagnostics.Process.GetCurrentProcess().SessionId);
                sb.AppendLine("os=" + Environment.OSVersion.VersionString);
                sb.AppendLine(Tts.TempReport.TrimEnd());
                sb.AppendLine("available=" + Tts.Available);
                sb.AppendLine("synth_ok=" + ok);
                sb.AppendLine("bytes=" + (wav == null ? 0 : wav.Length));
                sb.AppendLine("chain=" + chain);
                sb.AppendLine("voices=" + string.Join(" | ", Tts.Voices()));
                report = sb.ToString();
            }
            catch (Exception ex)
            {
                report = "self-test exception:\r\n" + ex;
            }

            string p1 = Path.Combine(Tts.TempDir(), "lamplight-tts-report.txt");
            string p2 = "";
            try { p2 = Path.Combine(Store.DataDir, "tts-report.txt"); } catch { }
            try { File.WriteAllText(p1, report, Encoding.UTF8); } catch { }
            if (!string.IsNullOrEmpty(p2)) { try { File.WriteAllText(p2, report, Encoding.UTF8); } catch { } }

            try
            {
                MessageBox.Show(report + "\r\n已保存到：\r\n" + p1,
                    "灯下 · 朗读自检", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        static string SafeTime(string path)
        {
            try { return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { return "-"; }
        }
    }
}
