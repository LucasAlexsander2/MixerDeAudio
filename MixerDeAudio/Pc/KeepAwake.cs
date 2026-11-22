using System;
using MixerDeAudio.Interop;

namespace MixerDeAudio.Pc;

/// <summary>
/// "Manter a tela ligada" (Fase 9b, D16): <c>SetThreadExecutionState</c> com o PC acordado e a tela acesa. O estado vale
/// para a thread que chamou, então é sempre a thread da tela. Só o <c>RealPc</c> usa (nunca no <c>--teste-pc</c>).
/// </summary>
internal static class KeepAwake
{
    /// <summary>Desliga sozinho depois disso.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(4);

    public static void Set(bool on)
    {
        var flags = NativeMethods.ES_CONTINUOUS;
        if (on) flags |= NativeMethods.ES_DISPLAY_REQUIRED | NativeMethods.ES_SYSTEM_REQUIRED;
        NativeMethods.SetThreadExecutionState(flags);
    }
}
