using System;

namespace MixerDeAudio.Services;

/// <summary>Regras puras de "Iniciar com o Windows" (D3), sem registro.</summary>
public static class StartupRules
{
    /// <summary>
    /// O valor da chave Run aponta para este .exe? Ignora maiúsculas e aspas; um argumento depois do caminho
    /// entre aspas é ignorado.
    /// </summary>
    public static bool PathMatches(string? runValue, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(runValue) || string.IsNullOrWhiteSpace(exePath)) return false;
        return string.Equals(Unquote(runValue), Unquote(exePath), StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string s)
    {
        var t = s.Trim();
        if (t.StartsWith('"'))
        {
            var end = t.IndexOf('"', 1);
            return end > 0 ? t.Substring(1, end - 1).Trim() : t.Trim('"').Trim();
        }
        return t;
    }

    /// <summary>
    /// Os bytes do <c>StartupApproved\Run</c> dizem que o app está desligado? Byte 0 ímpar (03, por exemplo) é
    /// desligado; 02 e 06 são ligado. Vazio ou nulo: não está desligado.
    /// </summary>
    public static bool IsDisabled(byte[]? approved)
    {
        if (approved is null || approved.Length == 0) return false;
        var b = approved[0];
        return b != 0x02 && b != 0x06 && (b & 1) == 1;
    }
}
