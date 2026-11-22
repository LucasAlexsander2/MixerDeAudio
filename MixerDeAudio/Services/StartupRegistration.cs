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

    /// <summary>Havia "Iniciar com o Windows" ligado, seja qual for o caminho gravado (Fase 11, D9: lido antes do desinstalador antigo).</summary>
    public static bool WasEnabledBefore()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return StartupRules.WasEnabled(run?.GetValue(ValueName) as string, approved?.GetValue(ValueName) as byte[]);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao ler iniciar com o Windows: {ex.Message}");
            return false;
        }
    }

    /// <summary>Hooks do Velopack (Fase 11, D3): se o valor existe e aponta para outro .exe, regrava para este. Não liga o que estava desligado.</summary>
    public static void RepointToCurrent()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            var value = StartupRules.RepointedValue(run?.GetValue(ValueName) as string, ExePath);
            if (run is not null && value is not null) run.SetValue(ValueName, value, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao corrigir iniciar com o Windows: {ex.Message}");
        }
    }

    /// <summary>Hook de desinstalação do Velopack (Fase 11, D3): tira o valor, mas só se ele aponta para este .exe.</summary>
    public static void RemoveIfCurrent()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(ValueName) is string value && StartupRules.PathMatches(value, ExePath))
                run.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao remover iniciar com o Windows: {ex.Message}");
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
                run.SetValue(ValueName, StartupRules.BuildRunValue(ExePath), RegistryValueKind.String);
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
