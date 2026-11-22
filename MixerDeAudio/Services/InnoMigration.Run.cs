using System;
using System.Diagnostics;
using System.IO;

namespace MixerDeAudio.Services;

public static partial class InnoMigration
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UninstallWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Executa a migração. SÓ pode ser chamada pelo hook <c>OnFirstRun</c> do Velopack (que nunca roda em <c>publicado\</c>).
    /// Falha vai só para o log: o app abre do mesmo jeito.
    /// </summary>
    internal static void Run()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // O estado do "Iniciar com o Windows" é lido ANTES do desinstalador, que apaga o valor.
            var plan = Plan(local, File.Exists, StartupRegistration.WasEnabledBefore());
            if (!plan.Migrate) return;
            Log.Write("migracao: instalacao antiga do Inno encontrada");

            AskOldAppToExit(plan.OldExe);
            Uninstall(plan.Uninstaller);

            if (plan.RestoreRun)
            {
                StartupRegistration.SetEnabled(true);
                Log.Write("migracao: iniciar com o Windows refeito para o caminho novo");
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO migracao do Inno: {ex.Message}");
        }
    }

    private static void AskOldAppToExit(string oldExe)
    {
        if (!File.Exists(oldExe)) return;
        try
        {
            using var asker = Process.Start(new ProcessStartInfo(oldExe, "--sair") { UseShellExecute = false });
            asker?.WaitForExit((int)ExitWait.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO migracao: pedir --sair ao app antigo: {ex.Message}");
        }
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < ExitWait && IsRunning(oldExe)) System.Threading.Thread.Sleep(100);
    }

    private static bool IsRunning(string exe)
    {
        foreach (var p in Process.GetProcessesByName("MixerDeAudio"))
        {
            using (p)
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch
                {
                    // sem acesso ao processo: não é o antigo do usuário
                }
            }
        }
        return false;
    }

    private static void Uninstall(string uninstaller)
    {
        using var p = Process.Start(new ProcessStartInfo(uninstaller, UninstallerArguments) { UseShellExecute = false });
        if (p is null) return;
        if (p.WaitForExit((int)UninstallWait.TotalMilliseconds))
            Log.Write($"migracao: desinstalador antigo terminou (codigo {p.ExitCode})");
        else
            Log.Write("ERRO migracao: o desinstalador antigo passou de 30 s; o app abre mesmo assim");
    }
}
