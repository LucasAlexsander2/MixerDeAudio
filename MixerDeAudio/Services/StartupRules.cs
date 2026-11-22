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

    /// <summary>Valor gravado na Run para um .exe: o caminho entre aspas, sem argumento (Fase 11, D9).</summary>
    public static string BuildRunValue(string exePath) => "\"" + exePath + "\"";

    /// <summary>
    /// Havia "Iniciar com o Windows" ligado (Fase 11, D9): existe o valor na Run e o StartupApproved não o desligou.
    /// Lido antes do desinstalador do Inno, que apaga o valor.
    /// </summary>
    public static bool WasEnabled(string? runValue, byte[]? approved) =>
        !string.IsNullOrWhiteSpace(runValue) && !IsDisabled(approved);

    /// <summary>
    /// Novo valor da Run depois de instalar ou atualizar (Fase 11, D3): nulo se não há valor ou se ele já aponta para
    /// este .exe; senão, o valor para este .exe.
    /// </summary>
    public static string? RepointedValue(string? runValue, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(runValue) || string.IsNullOrWhiteSpace(exePath)) return null;
        return PathMatches(runValue, exePath) ? null : BuildRunValue(exePath);
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
