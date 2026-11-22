using System;

namespace MixerDeAudio.Pc;

/// <summary>
/// Área de transferência só de texto (Fase 9d, D25). Esta parte é pura (corte do texto); o Win32 fica em
/// <c>ClipboardBridge.Native.cs</c>, e o <c>--teste-pc</c> usa uma variável em memória (D28).
/// </summary>
public static partial class ClipboardBridge
{
    public const int MaxChars = 15000;

    /// <summary>Corta em <see cref="MaxChars"/> sem partir um par substituto; <c>Cut</c> diz se cortou.</summary>
    public static (string Text, bool Cut) Limit(string? text)
    {
        if (string.IsNullOrEmpty(text)) return ("", false);
        if (text.Length <= MaxChars) return (text, false);
        var n = MaxChars;
        if (char.IsHighSurrogate(text[n - 1])) n--;
        return (text.Substring(0, n), true);
    }
}
