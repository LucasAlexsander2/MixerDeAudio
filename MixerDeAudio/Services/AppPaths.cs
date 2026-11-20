using System;
using System.IO;

namespace MixerDeAudio.Services;

/// <summary>Pastas de dados do app (A8): <c>%LOCALAPPDATA%\MixerDeAudio\</c>.</summary>
internal static class AppPaths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MixerDeAudio");

    public static string ConfigFile { get; } = Path.Combine(DataFolder, "config.json");

    public static string LogsFolder { get; } = Path.Combine(DataFolder, "logs");

    public static string LogFile { get; } = Path.Combine(LogsFolder, "mixer.log");

    public static string OldLogFile { get; } = Path.Combine(LogsFolder, "mixer.old.log");
}
