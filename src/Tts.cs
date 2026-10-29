using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Lamplight
{
    /// <summary>
    /// 语音朗读。两条路径：
    /// 1) System.Speech 合成到内存 WAV，再用 SoundPlayer 播放（不经过 SAPI 的播放设备）；
    /// 2) 直接 SAPI SpVoice.Speak（部分机器上会被拒绝，作为兜底）。
    /// COM 对象一律在 STA 线程内创建和使用。
    /// </summary>
    static class Tts
    {
        static bool _probed;
        static string[] _voices = new string[0];
        static bool _available;
        static readonly object Gate = new object();
        static Assembly _speech;
        static bool _speechTried;
        static SoundPlayer _player;
        static System.Diagnostics.Process _extPlay;
        public static bool LastPlayTooFast;
        public static volatile bool CancelPlayback;
        public static string LastPlayError = "";
        static bool _waveOutBroken;

        public static string LastSynthError = "";
        static string _tempDir;

        /// <summary>找一个确实可写的临时目录：程序目录优先，其次 LocalAppData，最后系统 TEMP。</summary>
        public static string TempDir()
        {
            if (_tempDir != null) return _tempDir;
            string[] cands = new string[]
            {
                Path.Combine(Store.DataDir, "tmp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lamplight", "tmp"),
                Path.GetTempPath()
            };
            foreach (string c in cands)
            {
                try
                {
                    Directory.CreateDirectory(c);
                    string probe = Path.Combine(c, "w.tmp");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    _tempDir = c;
                    return c;
                }
                catch { }
            }
            _tempDir = Path.GetTempPath();
            return _tempDir;
        }

        public static string TempReport
        {
            get
            {
                try
                {
                    return "tempdir=" + TempDir() + " (writable)\r\n" +
                           "env_temp=" + Path.GetTempPath() + "\r\n" +
                           "data_dir=" + Store.DataDir + "\r\n";
                }
                catch { return ""; }
            }
        }

        public static bool Available
        {
            get { Probe(); return _available; }
        }

        public static string[] Voices()
        {
            Probe();
            return _voices;
        }

        static Assembly SpeechAssembly()
        {
            if (!_speechTried)
            {
                _speechTried = true;
                string[] names = {
                    "System.Speech",
                    "System.Speech, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"
                };
                foreach (string n in names)
                {
                    try { _speech = Assembly.Load(n); if (_speech != null) break; }
                    catch { }
                }
                if (_speech == null)
                {
                    try
                    {
                        string dir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
                        string f = Path.Combine(dir, "System.Speech.dll");
                        if (File.Exists(f)) _speech = Assembly.LoadFrom(f);
                    }
                    catch { }
                }
                if (_speech == null)
                {
                    try
                    {
                        string gac = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                            @"Microsoft.NET\assembly\GAC_MSIL\System.Speech");
                        if (Directory.Exists(gac))
                            foreach (string d in Directory.GetDirectories(gac))
                                foreach (string f in Directory.GetFiles(d, "System.Speech.dll"))
                                { _speech = Assembly.LoadFrom(f); break; }
                    }
                    catch { }
                }
            }
            return _speech;
        }

        /// <summary>SAPI 合成到内存流（不直接用播放设备）。</summary>
        public static bool SynthSapi(string text, int rate, int volume, out byte[] wav, out string error, bool useFile)
        {
            byte[] result = null;
            string err = null;
            Thread t = new Thread(delegate()
            {
                object voice = null, outObj = null;
                string tmp = Path.Combine(TempDir(), "ink-tts-" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    Type vt = Type.GetTypeFromProgID("SAPI.SpVoice");
                    voice = Activator.CreateInstance(vt);
                    dynamic v = voice;
                    v.Rate = Math.Max(-10, Math.Min(10, rate));
                    v.Volume = Math.Max(0, Math.Min(100, volume));

                    if (useFile)
                    {
                        Type ft = Type.GetTypeFromProgID("SAPI.SpFileStream");
                        dynamic fs = Activator.CreateInstance(ft);
                        outObj = (object)fs;
                        fs.Open(tmp, 3, false);
                        v.AudioOutputStream = fs;
                        v.Speak(text, 0);
                        fs.Close();
                        result = File.ReadAllBytes(tmp);
                    }
                    else
                    {
                        Type mt = Type.GetTypeFromProgID("SAPI.SpMemoryStream");
                        dynamic ms = Activator.CreateInstance(mt);
                        outObj = (object)ms;
                        try { ms.Format.Type = 22; } catch { }
                        v.AudioOutputStream = ms;
                        v.Speak(text, 0);
                        object data = ms.GetData();
                        result = data as byte[];
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                finally
                {
                    try { if (voice != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(voice); } catch { }
                    try { if (outObj != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(outObj); } catch { }
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(6000);
            wav = result;
            error = err;
            return result != null && result.Length > 44;
        }

        /// <summary>调用同目录的 32 位助手合成（有些机器只装了 32 位语音）。</summary>
        public static bool SynthViaHelper(string text, int rate, int volume, out byte[] wav, out string error)
        {
            wav = null;
            error = null;
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string helper = Path.Combine(dir, "LamplightTts32.exe");
                if (!File.Exists(helper)) { error = "缺少 LamplightTts32.exe"; return false; }
                string tag32 = Guid.NewGuid().ToString("N");
                string txt = Path.Combine(TempDir(), "tts32-" + tag32 + ".txt");
                string outwav = Path.Combine(TempDir(), "tts32-" + tag32 + ".wav");
                File.WriteAllText(txt, text, new UTF8Encoding(false));
                if (File.Exists(outwav)) File.Delete(outwav);

                ProcessStartInfo psi = new ProcessStartInfo(helper,
                    "--text \"" + txt + "\" --out \"" + outwav + "\" --rate " + rate + " --vol " + volume);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                string stderr = "";
                using (Process proc = Process.Start(psi))
                {
                    stderr = proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(30000))
                    {
                        try { proc.Kill(); } catch { }
                        error = "32 位助手超时";
                        return false;
                    }
                    if (proc.ExitCode != 0)
                    {
                        error = "32 位助手退出码 " + proc.ExitCode + (stderr.Length > 0 ? "：" + stderr.Trim() : "");
                        return false;
                    }
                }
                if (!File.Exists(outwav)) { error = "32 位助手没有生成文件"; return false; }
                wav = File.ReadAllBytes(outwav);
                return wav.Length > 44;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>用外部 PowerShell 合成（绕开本进程的 COM/权限限制）。</summary>
        public static bool SynthViaPowerShell(string text, int rate, int volume, string voiceName, out byte[] wav, out string error)
        {
            wav = null;
            error = null;
            string tag = Guid.NewGuid().ToString("N");
            string txtPath = Path.Combine(TempDir(), "ink-tts-" + tag + ".txt");
            string wavPath = Path.Combine(TempDir(), "ink-tts-" + tag + ".wav");
            string ps1Path = Path.Combine(TempDir(), "ink-tts-" + tag + ".ps1");
            try
            {
                File.WriteAllText(txtPath, text, new UTF8Encoding(false));
                if (File.Exists(wavPath)) File.Delete(wavPath);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("$ErrorActionPreference='Stop'");
                sb.AppendLine("Add-Type -AssemblyName System.Speech");
                sb.AppendLine("$t = Get-Content -Raw -Encoding UTF8 '" + txtPath.Replace("'", "''") + "'");
                sb.AppendLine("$s = New-Object System.Speech.Synthesis.SpeechSynthesizer");
                sb.AppendLine("$s.Rate = " + Math.Max(-10, Math.Min(10, rate)));
                sb.AppendLine("$s.Volume = " + Math.Max(0, Math.Min(100, volume)));
                if (!string.IsNullOrEmpty(voiceName))
                    sb.AppendLine("try { $s.SelectVoice('" + voiceName.Replace("'", "''") + "') } catch { }");
                sb.AppendLine("$s.SetOutputToWaveFile('" + wavPath.Replace("'", "''") + "')");
                sb.AppendLine("$s.Speak($t)");
                sb.AppendLine("$s.Dispose()");
                File.WriteAllText(ps1Path, sb.ToString(), new UTF8Encoding(true));

                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1Path + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process proc = Process.Start(psi))
                {
                    if (!proc.WaitForExit(30000))
                    {
                        try { proc.Kill(); } catch { }
                        error = "外部合成超时";
                        return false;
                    }
                    if (proc.ExitCode != 0)
                    {
                        error = "外部合成退出码 " + proc.ExitCode;
                        return false;
                    }
                }
                if (!File.Exists(wavPath)) { error = "外部合成没有生成文件"; return false; }
                wav = File.ReadAllBytes(wavPath);
                if (wav.Length <= 44) { error = "外部合成结果为空"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>用外部进程播放 WAV（本进程没有音频权限时的最后手段）。</summary>
        public static bool PlayViaPowerShell(byte[] wav, out string error)
        {
            error = null;
            try
            {
                string wavPath = Path.Combine(TempDir(), "ink-tts-play-" + Guid.NewGuid().ToString("N") + ".wav");
                File.WriteAllBytes(wavPath, wav);
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"(New-Object Media.SoundPlayer '" +
                    wavPath.Replace("'", "''") + "').PlaySync()\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process proc = Process.Start(psi);
                _extPlay = proc;
                proc.WaitForExit(120000);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>依次尝试三种合成方式，返回 WAV 与尝试报告。</summary>
        public static bool SynthChain(string text, int rate, int volume, string voiceName, out byte[] wav, out string report)
        {
            wav = null;
            report = "";
            string err;
            if (SynthViaHelper(text, rate, volume, out wav, out err)) { report = "32 位助手 OK"; return true; }
            report += "32 位助手: " + (err ?? "失败") + "; ";

            if (SynthSapi(text, rate, volume, out wav, out err, false)) { report += "SAPI 内存流 OK"; return true; }
            report += "SAPI 内存流: " + (err ?? "失败") + "; ";

            if (SynthToWave(text, rate, volume, voiceName, out wav, out err)) { report += "System.Speech OK"; return true; }
            report += "System.Speech: " + (err ?? "失败") + "; ";

            if (SynthSapi(text, rate, volume, out wav, out err, true)) { report += "SAPI 文件流 OK"; return true; }
            report += "SAPI 文件流: " + (err ?? "失败") + "; ";

            if (SynthViaPowerShell(text, rate, volume, voiceName, out wav, out err)) { report += "外部进程合成 OK"; return true; }
            report += "外部进程: " + (err ?? "失败");
            return false;
        }

        public static void Probe()
        {
            if (_probed) return;
            _probed = true;
            Thread t = new Thread(delegate()
            {
                try
                {
                    dynamic v = CreateVoice(0, 90);
                    List<string> list = new List<string>();
                    dynamic tokens = v.GetVoices();
                    int n = tokens.Count;
                    for (int i = 0; i < n; i++)
                        list.Add((string)tokens.Item(i).GetDescription());
                    lock (Gate) { _voices = list.ToArray(); _available = true; }
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(v); } catch { }
                }
                catch
                {
                    lock (Gate) { _available = false; }
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(2500);
        }

        public static dynamic CreateVoice(int rate, int volume)
        {
            Type type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type == null) throw new InvalidOperationException("SAPI 不可用");
            dynamic v = Activator.CreateInstance(type);
            v.Rate = Math.Max(-10, Math.Min(10, rate));
            v.Volume = Math.Max(0, Math.Min(100, volume));
            return v;
        }

        /// <summary>把一句话合成成 WAV（System.Speech），不播放。</summary>
        public static bool SynthToWave(string text, int rate, int volume, string voiceName, out byte[] wav, out string error)
        {
            wav = null;
            error = null;
            Assembly asm = SpeechAssembly();
            if (asm == null) { error = "System.Speech 不可用"; LastSynthError = error; return false; }
            try
            {
                Type type = asm.GetType("System.Speech.Synthesis.SpeechSynthesizer");
                if (type == null) { error = "找不到 SpeechSynthesizer"; LastSynthError = error; return false; }
                object synth = Activator.CreateInstance(type);
                try
                {
                    if (!string.IsNullOrEmpty(voiceName))
                    {
                        MethodInfo sel = type.GetMethod("SelectVoice", new Type[] { typeof(string) });
                        if (sel != null) sel.Invoke(synth, new object[] { voiceName });
                    }
                    PropertyInfo pr = type.GetProperty("Rate");
                    if (pr != null) pr.SetValue(synth, Math.Max(-10, Math.Min(10, rate)), null);
                    PropertyInfo pv = type.GetProperty("Volume");
                    if (pv != null) pv.SetValue(synth, Math.Max(0, Math.Min(100, volume)), null);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        MethodInfo setOut = type.GetMethod("SetOutputToWaveStream");
                        if (setOut == null) { error = "SetOutputToWaveStream 不存在"; LastSynthError = error; return false; }
                        setOut.Invoke(synth, new object[] { ms });
                        MethodInfo speak = type.GetMethod("Speak", new Type[] { typeof(string) });
                        speak.Invoke(synth, new object[] { text });
                        wav = ms.ToArray();
                    }
                }
                finally
                {
                    IDisposable d = synth as IDisposable;
                    if (d != null) d.Dispose();
                }
                if (wav == null || wav.Length <= 44) { error = "合成结果为空"; LastSynthError = error; return false; }
                LastSynthError = "";
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = inner.Message + " (" + inner.HResult.ToString("X8") + ")";
                LastSynthError = error;
                return false;
            }
        }

        /// <summary>裁掉尾部静音，并重建成标准 44 字节头 WAV。</summary>
        public static byte[] TrimSilence(byte[] wav)
        {
            try
            {
                if (wav == null || wav.Length < 48) return wav;
                int fmtOff = -1, dataOff = -1, dataSize = 0;
                int i = 12;
                while (i + 8 <= wav.Length)
                {
                    string id = "" + (char)wav[i] + (char)wav[i + 1] + (char)wav[i + 2] + (char)wav[i + 3];
                    int size = BitConverter.ToInt32(wav, i + 4);
                    if (size < 0) return wav;
                    if (id == "fmt ") fmtOff = i + 8;
                    else if (id == "data") { dataOff = i + 8; dataSize = Math.Min(size, wav.Length - dataOff); break; }
                    i += 8 + size + (size & 1);
                }
                if (fmtOff < 0 || dataOff < 0 || dataSize <= 0) return wav;
                if (fmtOff + 16 > wav.Length) return wav;

                int formatTag = BitConverter.ToInt16(wav, fmtOff);
                int channels = BitConverter.ToInt16(wav, fmtOff + 2);
                int sampleRate = BitConverter.ToInt32(wav, fmtOff + 4);
                int byteRate = BitConverter.ToInt32(wav, fmtOff + 8);
                int blockAlign = BitConverter.ToInt16(wav, fmtOff + 12);
                int bits = BitConverter.ToInt16(wav, fmtOff + 14);
                // 0xFFFE = WAVE_FORMAT_EXTENSIBLE，只要位深是 16 位就按 PCM 重装
                if ((formatTag != 1 && formatTag != -2) || channels <= 0 || sampleRate <= 0 ||
                    bits != 16 || blockAlign <= 0)
                    return wav;
                if (byteRate <= 0) byteRate = sampleRate * blockAlign;

                int frame = blockAlign;
                int frames = dataSize / frame;
                int keepFrames = frames;
                if (frames >= 8)
                {
                    int lastLoud = -1;
                    for (int f = frames - 1; f >= 0; f--)
                    {
                        int off = dataOff + f * frame;
                        bool loud = false;
                        for (int c = 0; c < channels; c++)
                        {
                            short v = BitConverter.ToInt16(wav, off + c * 2);
                            if (Math.Abs((int)v) > 80) { loud = true; break; }
                        }
                        if (loud) { lastLoud = f; break; }
                    }
                    if (lastLoud >= 0)
                        keepFrames = Math.Min(frames, lastLoud + 1 + (int)(0.18 * Math.Max(8000, sampleRate)));
                }
                int newSize = keepFrames * frame;
                if (newSize <= 0) return wav;

                // 一律重建成标准 44 字节 PCM 头，SoundPlayer / waveOut 都认
                byte[] outp = new byte[44 + newSize];
                Encoding.ASCII.GetBytes("RIFF").CopyTo(outp, 0);
                BitConverter.GetBytes(36 + newSize).CopyTo(outp, 4);
                Encoding.ASCII.GetBytes("WAVE").CopyTo(outp, 8);
                Encoding.ASCII.GetBytes("fmt ").CopyTo(outp, 12);
                BitConverter.GetBytes(16).CopyTo(outp, 16);
                BitConverter.GetBytes((short)1).CopyTo(outp, 20);
                BitConverter.GetBytes((short)channels).CopyTo(outp, 22);
                BitConverter.GetBytes(sampleRate).CopyTo(outp, 24);
                BitConverter.GetBytes(byteRate).CopyTo(outp, 28);
                BitConverter.GetBytes((short)blockAlign).CopyTo(outp, 32);
                BitConverter.GetBytes((short)bits).CopyTo(outp, 34);
                Encoding.ASCII.GetBytes("data").CopyTo(outp, 36);
                BitConverter.GetBytes(newSize).CopyTo(outp, 40);
                Array.Copy(wav, dataOff, outp, 44, newSize);
                return outp;
            }
            catch { return wav; }
        }

        public static double WavSeconds(byte[] wav)
        {
            try
            {
                if (wav.Length < 44) return 0;
                int byteRate = 0, dataSize = 0;
                int i = 12;
                while (i + 8 <= wav.Length)
                {
                    string id = "" + (char)wav[i] + (char)wav[i + 1] + (char)wav[i + 2] + (char)wav[i + 3];
                    int size = BitConverter.ToInt32(wav, i + 4);
                    if (id == "fmt ") byteRate = BitConverter.ToInt32(wav, i + 16);
                    else if (id == "data") { dataSize = size; break; }
                    i += 8 + size + (size & 1);
                }
                if (byteRate <= 0 || dataSize <= 0) return 0;
                return (double)dataSize / byteRate;
            }
            catch { return 0; }
        }

        static bool WavLoadable(byte[] wav)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(wav))
                using (SoundPlayer sp = new SoundPlayer(ms)) { sp.Load(); }
                return true;
            }
            catch { return false; }
        }

        public static bool PlayWav(byte[] wav)
        {
            StopPlayback();
            CancelPlayback = false;
            LastPlayError = "";
            if (wav == null || wav.Length < 44) { LastPlayError = "WAV 为空"; return false; }

            byte[] orig = wav;
            byte[] trimmed = TrimSilence(wav);
            // 裁剪后的文件播放器不认，就退回原始 WAV，宁可多留一点静音也不能没声
            if (!ReferenceEquals(trimmed, orig) && !WavLoadable(trimmed)) trimmed = orig;
            wav = trimmed;

            DateTime t0 = DateTime.Now;
            bool ok = false;
            if (WaveOut.Play(wav)) ok = true;
            else if (!CancelPlayback)
            {
                try
                {
                    MemoryStream ms = new MemoryStream(wav);
                    SoundPlayer sp = new SoundPlayer(ms);
                    _player = sp;
                    sp.PlaySync();
                    ok = true;
                }
                catch (Exception ex) { LastPlayError = "SoundPlayer: " + ex.Message; ok = false; }
                finally { _player = null; }
            }
            if (!ok && LastPlayError.Length == 0) LastPlayError = "waveOut 与 SoundPlayer 都失败";
            LastPlayTooFast = (DateTime.Now - t0).TotalMilliseconds < 200 && wav.Length > 20000;
            return ok;
        }

        public static void StopPlayback()
        {
            CancelPlayback = true;
            try { WaveOut.Stop(); } catch { }
            try { if (_player != null) _player.Stop(); } catch { }
            try { if (_extPlay != null && !_extPlay.HasExited) _extPlay.Kill(); } catch { }
        }

        /// <summary>生成一段 440Hz 测试音并播放，返回 "ok/耗时" 或错误。</summary>
        public static string PlayBeep()
        {
            try
            {
                int rate = 22050, seconds = 1;
                int n = rate * seconds;
                byte[] data = new byte[n * 2];
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / rate;
                    double fade = Math.Min(1, Math.Min(i / 400.0, (n - i) / 400.0));
                    short v = (short)(Math.Sin(2 * Math.PI * 440 * t) * 9000 * fade);
                    data[i * 2] = (byte)(v & 0xFF);
                    data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                byte[] wav = BuildWav(data, rate, 1, 16);
                DateTime t0 = DateTime.Now;
                bool ok = PlayWav(wav);
                double ms = (DateTime.Now - t0).TotalMilliseconds;
                return ok ? (ms > 300 ? "ok " + (int)ms + "ms" : "returned too fast (" + (int)ms + "ms, 可能没有音频设备)")
                          : "PlaySync 失败";
            }
            catch (Exception ex) { return "error: " + ex.Message; }
        }

        static byte[] BuildWav(byte[] pcm, int rate, int channels, int bits)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                int byteRate = rate * channels * bits / 8;
                w.Write(new char[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + pcm.Length);
                w.Write(new char[] { 'W', 'A', 'V', 'E' });
                w.Write(new char[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(byteRate);
                w.Write((short)(channels * bits / 8));
                w.Write((short)bits);
                w.Write(new char[] { 'd', 'a', 't', 'a' });
                w.Write(pcm.Length);
                w.Write(pcm);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>试听：合成一句并播放；返回空串表示成功，否则返回错误。</summary>
        public static string Preview(int rate, int volume, string voiceName, string text)
        {
            byte[] wav;
            string err;
            string report;
            if (SynthChain(text, rate, volume, voiceName, out wav, out report))
            {
                Thread t = new Thread(delegate() { PlayWav(wav); });
                t.IsBackground = true;
                t.Start();
                return "";
            }
            string sapiErr = "";
            Thread t2 = new Thread(delegate()
            {
                try
                {
                    dynamic v = CreateVoice(rate, volume);
                    v.Speak(text, 0);
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(v); } catch { }
                }
                catch (Exception ex) { sapiErr = ex.Message; }
            });
            t2.IsBackground = true;
            t2.SetApartmentState(ApartmentState.STA);
            t2.Start();
            t2.Join(4000);
            return string.IsNullOrEmpty(sapiErr) ? "" : "合成失败：" + report + "；直读失败：" + sapiErr;
        }

        public static bool SelfTest()
        {
            bool ok = false;
            Thread t = new Thread(delegate()
            {
                try
                {
                    dynamic v = CreateVoice(0, 40);
                    v.Rate = 1;
                    v.Volume = 50;
                    ok = true;
                }
                catch { ok = false; }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join(4000);
            return ok;
        }

        /// <summary>按句切分，括号/引号内的标点不切（【】「」『』“”（））。</summary>
        static bool IsTrailingPunct(char c)
        {
            switch (c)
            {
                case '”': case '’': case '」': case '』': case '）': case ')':
                case '】': case '》': case '〉': case '"': case '\'':
                case '。': case '！': case '？': case '!': case '?': case '…':
                case '，': case ',': case '、': case '；': case ';': case '：': case ':':
                    return true;
            }
            return false;
        }

        static bool HasContent(string s)
        {
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) return true;
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }
            return false;
        }

        /// <summary>按句切分：引号内的句号也断句，收尾引号并入本句。</summary>
        public static List<int[]> Split(string text)
        {
            List<int[]> spans = new List<int[]>();
            if (string.IsNullOrEmpty(text)) return spans;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool brk = c == '\n' || c == '\r' ||
                           c == '。' || c == '！' || c == '？' || c == '!' || c == '?' || c == '…';
                if (!brk) continue;

                int end = i + 1;
                // 句末标点、收尾引号/括号一起并入本句
                while (end < text.Length && IsTrailingPunct(text[end])) end++;
                AddSpan(spans, text, start, end - start);
                start = end;
                i = end - 1;
            }
            if (start < text.Length) AddSpan(spans, text, start, text.Length - start);
            if (spans.Count == 0) spans.Add(new int[] { 0, text.Length });
            return CapSpans(spans, text, 110);
        }

        /// <summary>长句再按逗号/分号切开，避免一次合成太长导致超时被整句跳过。</summary>
        static List<int[]> CapSpans(List<int[]> spans, string text, int maxLen)
        {
            List<int[]> outp = new List<int[]>();
            foreach (int[] sp in spans)
            {
                int start = sp[0], len = sp[1];
                while (len > maxLen)
                {
                    int cut = -1;
                    int limit = Math.Min(len - 1, maxLen);
                    for (int k = limit; k >= maxLen / 3; k--)
                    {
                        char c = text[start + k];
                        if (c == '，' || c == ',' || c == '、' || c == '；' || c == ';' || c == '：' || c == ':')
                        { cut = k + 1; break; }
                    }
                    if (cut < 0) cut = limit;
                    outp.Add(new int[] { start, cut });
                    start += cut;
                    len -= cut;
                }
                if (len > 0) outp.Add(new int[] { start, len });
            }
            return outp;
        }

        static void AddSpan(List<int[]> spans, string text, int start, int len)
        {
            if (len <= 0) return;
            string piece = text.Substring(start, len);
            if (piece.Trim().Length == 0) return;
            if (!HasContent(piece))
            {
                if (spans.Count > 0)
                {
                    int[] last = spans[spans.Count - 1];
                    if (last[0] + last[1] == start) { last[1] += len; return; }
                }
                return;
            }
            spans.Add(new int[] { start, len });
        }
    }
}
