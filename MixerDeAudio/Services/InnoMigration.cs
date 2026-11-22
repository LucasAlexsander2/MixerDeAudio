using System;
using System.IO;

namespace MixerDeAudio.Services;

/// <summary>O que fazer com a instalação antiga do Inno Setup (Fase 11, D9).</summary>
/// <param name="Migrate">Há instalação antiga: fechar, desinstalar e refazer o "Iniciar com o Windows".</param>
/// <param name="OldExe">O <c>MixerDeAudio.exe</c> antigo (para pedir <c>--sair</c>).</param>
/// <param name="Uninstaller">O <c>unins000.exe</c> antigo.</param>
/// <param name="RestoreRun">Regravar o <c>Run</c> para o caminho novo, depois do desinstalador.</param>
public sealed record InnoMigrationPlan(bool Migrate, string OldExe, string Uninstaller, bool RestoreRun);

/// <summary>
/// Migração do Inno (Fase 11, D9): a regra pura (esta parte) e a execução (<c>InnoMigration.Run.cs</c>, só no app).
/// A regra recebe caminhos e estados, nunca toca o disco nem o registro, e por isso é testada com valores falsos.
/// </summary>
public static partial class InnoMigration
{
    public const string UninstallerName = "unins000.exe";
    public const string ExeName = "MixerDeAudio.exe";

    /// <summary>Pasta da instalação antiga: <c>%LOCALAPPDATA%\Programs\MixerDeAudio</c>.</summary>
    public static string LegacyFolder(string localAppData) => Path.Combine(localAppData, "Programs", "MixerDeAudio");

    /// <summary>
    /// Decide a migração. <paramref name="fileExists"/> diz se um arquivo existe; <paramref name="runWasEnabled"/> é o estado do
    /// "Iniciar com o Windows" lido <b>antes</b> de qualquer passo (o desinstalador antigo apaga o valor).
    /// </summary>
    public static InnoMigrationPlan Plan(string localAppData, Func<string, bool> fileExists, bool runWasEnabled)
    {
        var folder = LegacyFolder(localAppData);
        var uninstaller = Path.Combine(folder, UninstallerName);
        var oldExe = Path.Combine(folder, ExeName);
        var has = fileExists(uninstaller);
        return new InnoMigrationPlan(has, oldExe, uninstaller, has && runWasEnabled);
    }

    /// <summary>Argumentos do desinstalador antigo (silencioso, sem mensagens, sem reiniciar).</summary>
    public const string UninstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
}
