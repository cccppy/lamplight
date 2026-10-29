using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace LamplightTts32
{
    /// <summary>
    /// 32 位语音助手：用 System.Speech（32 位）把文本合成为 WAV。
    /// 参数：--text &lt;txt文件&gt; --out &lt;wav文件&gt; [--rate -10..10] [--vol 0..100]
    /// 退出码：0 成功；2 合成异常；3 输出为空；4 参数错误
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            string textFile = null, outFile = null;
            int rate = 0, vol = 90;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--text": if (i + 1 < args.Length) textFile = args[++i]; break;
                    case "--out": if (i + 1 < args.Length) outFile = args[++i]; break;
                    case "--rate": if (i + 1 < args.Length) int.TryParse(args[++i], out rate); break;
                    case "--vol": if (i + 1 < args.Length) int.TryParse(args[++i], out vol); break;
                }
            }
            if (string.IsNullOrEmpty(textFile) || string.IsNullOrEmpty(outFile)) return 4;
            string text;
            try { text = File.ReadAllText(textFile, Encoding.UTF8); }
            catch (Exception ex) { Console.Error.WriteLine("read: " + ex.Message); return 4; }

            try
            {
                Assembly asm = Assembly.Load("System.Speech");
                Type type = asm.GetType("System.Speech.Synthesis.SpeechSynthesizer");
                object synth = Activator.CreateInstance(type);
                try
                {
                    PropertyInfo pr = type.GetProperty("Rate");
                    if (pr != null) pr.SetValue(synth, Math.Max(-10, Math.Min(10, rate)), null);
                    PropertyInfo pv = type.GetProperty("Volume");
                    if (pv != null) pv.SetValue(synth, Math.Max(0, Math.Min(100, vol)), null);
                    // 强制 22.05kHz / 16 位 / 单声道 PCM，避免扩展格式导致播放器不认
                    Type fmtType = asm.GetType("System.Speech.AudioFormat.SpeechAudioFormatInfo");
                    Type bitsType = asm.GetType("System.Speech.AudioFormat.AudioBitsPerSample");
                    Type chanType = asm.GetType("System.Speech.AudioFormat.AudioChannel");
                    object fmt = Activator.CreateInstance(fmtType, new object[] {
                        22050,
                        Enum.Parse(bitsType, "Sixteen"),
                        Enum.Parse(chanType, "Mono")
                    });
                    type.GetMethod("SetOutputToWaveFile", new Type[] { typeof(string), fmtType })
                        .Invoke(synth, new object[] { outFile, fmt });
                    type.GetMethod("Speak", new Type[] { typeof(string) })
                        .Invoke(synth, new object[] { text });
                }
                finally
                {
                    IDisposable d = synth as IDisposable;
                    if (d != null) d.Dispose();
                }
                if (!File.Exists(outFile) || new FileInfo(outFile).Length <= 44) return 3;
                return 0;
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Console.Error.WriteLine(inner.GetType().Name + ": " + inner.Message);
                return 2;
            }
        }
    }
}
