using System;
using Microsoft.Win32;

namespace MixerDeAudio.Services;

/// <summary>
/// "Iniciar com o Windows" (D3): valor <c>MixerDeAudio</c> em <c>HKCU\...\Run</c>. A fonte da verdade é o registro,
/// porque o Gerenciador de Tarefas também pode desligar o app (<c>StartupApproved\Run</c>). Só este valor é tocado.
/// A parte pura está em <see cref="StartupRules"/>.
/// </summary>
internal static class StartupRegistration
{
    public const string ValueName = "MixerDeAudio";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static string ExePath => Environment.ProcessPath ?? string.Empty;

    /// <summary>Ligado = valor na Run apontando para este .exe e não desligado no StartupApproved.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string value || !StartupRules.PathMatches(value, ExePath)) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            var bytes = approved?.GetValue(ValueName) as byte[];
            return !StartupRules.IsDisabled(bytes);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao ler iniciar com o Windows: {ex.Message}");
            return false;
        }
    }

    /// <summary>Liga (grava o caminho entre aspas, sem argumento, e tira o "desligado") ou desliga (apaga o valor).</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                run.SetValue(ValueName, "\"" + ExePath + "\"", RegistryValueKind.String);
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
                if (approved?.GetValue(ValueName) is not null) approved.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao gravar iniciar com o Windows: {ex.Message}");
            return false;
        }
    }
}
