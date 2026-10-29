using System;

namespace Lamplight
{
    public partial class MainForm
    {
        static readonly int[] TtsMinutes = { 0, 5, 15, 30, 60, 120 };

        static int PrevMinute(int cur, int dir)
        {
            int i = Array.IndexOf(TtsMinutes, cur);
            if (i < 0) i = 0;
            i = Math.Max(0, Math.Min(TtsMinutes.Length - 1, i + dir));
            return TtsMinutes[i];
        }
    }
}
