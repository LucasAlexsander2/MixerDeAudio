using System;
using MixerDeAudio.Interop;

namespace MixerDeAudio.Services;

/// <summary>Devolve ao Windows a memória que o app não está usando (D17).</summary>
internal static class MemoryTrimmer
{
    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        // -1, -1: o Windows tira da memória física tudo o que puder do processo.
        NativeMethods.SetProcessWorkingSetSizeEx(NativeMethods.GetCurrentProcess(), -1, -1, 0);
    }
}
