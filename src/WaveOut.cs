using System;
using System.Runtime.InteropServices;

namespace Lamplight
{
    /// <summary>用 waveOut 直接播放 PCM WAV，可随时硬停止（SoundPlayer 做不到）。</summary>
    static class WaveOut
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        const uint WHDR_DONE = 0x00000001;
        static readonly object Gate = new object();
        static IntPtr _handle = IntPtr.Zero;
        static volatile bool _stop;

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint dev, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, uint size);

        public static void Stop()
        {
            _stop = true;
            lock (Gate)
            {
                try { if (_handle != IntPtr.Zero) waveOutReset(_handle); } catch { }
            }
        }

        /// <summary>同步播放，返回 true 表示完整播完；被停止返回 false。</summary>
        public static bool Play(byte[] wav)
        {
            if (wav == null || wav.Length < 44) return false;
            _stop = false;
            IntPtr h = IntPtr.Zero, hdr = IntPtr.Zero, data = IntPtr.Zero;
            try
            {
                int channels = BitConverter.ToInt16(wav, 22);
                int rate = BitConverter.ToInt32(wav, 24);
                int bits = BitConverter.ToInt16(wav, 34);
                int dataSize = wav.Length - 44;
                if (channels <= 0 || rate <= 0 || dataSize <= 0) return false;

                WAVEFORMATEX fmt = new WAVEFORMATEX();
                fmt.wFormatTag = 1;
                fmt.nChannels = (ushort)channels;
                fmt.nSamplesPerSec = (uint)rate;
                fmt.wBitsPerSample = (ushort)bits;
                fmt.nBlockAlign = (ushort)(channels * bits / 8);
                fmt.nAvgBytesPerSec = (uint)(rate * fmt.nBlockAlign);

                if (waveOutOpen(out h, 0, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) return false;

                data = Marshal.AllocHGlobal(dataSize);
                Marshal.Copy(wav, 44, data, dataSize);

                WAVEHDR wh = new WAVEHDR();
                wh.lpData = data;
                wh.dwBufferLength = (uint)dataSize;
                hdr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WAVEHDR)));
                Marshal.StructureToPtr(wh, hdr, false);

                lock (Gate) { _handle = h; }
                if (waveOutPrepareHeader(h, hdr, (uint)Marshal.SizeOf(typeof(WAVEHDR))) != 0) return false;
                if (waveOutWrite(h, hdr, (uint)Marshal.SizeOf(typeof(WAVEHDR))) != 0) return false;

                bool stopped = false;
                while (true)
                {
                    if (_stop) { stopped = true; break; }
                    WAVEHDR cur = (WAVEHDR)Marshal.PtrToStructure(hdr, typeof(WAVEHDR));
                    if ((cur.dwFlags & WHDR_DONE) != 0)
                    {
                        System.Threading.Thread.Sleep(80);   // 给尾音留一点时间，避免最后一个字被切
                        break;
                    }
                    System.Threading.Thread.Sleep(20);
                }
                if (stopped)
                {
                    try { waveOutReset(h); } catch { }
                    return false;
                }
                return true;
            }
            catch { return false; }
            finally
            {
                lock (Gate) { _handle = IntPtr.Zero; }
                try { if (h != IntPtr.Zero) waveOutReset(h); } catch { }
                try { if (h != IntPtr.Zero && hdr != IntPtr.Zero) waveOutUnprepareHeader(h, hdr, (uint)Marshal.SizeOf(typeof(WAVEHDR))); } catch { }
                try { if (h != IntPtr.Zero) waveOutClose(h); } catch { }
                try { if (hdr != IntPtr.Zero) Marshal.FreeHGlobal(hdr); } catch { }
                try { if (data != IntPtr.Zero) Marshal.FreeHGlobal(data); } catch { }
            }
        }
    }
}
