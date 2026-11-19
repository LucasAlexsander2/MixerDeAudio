using System;
using System.IO;
using System.Text;

namespace MixerDeAudio.Services;

/// <summary>Log mínimo (D10): uma linha por evento, gravação síncrona e curta, sem timer.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    /// <summary>Se o log passou de 1 MB, vira <c>mixer.old.log</c>. Chamado uma vez, ao iniciar.</summary>
    public static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(AppPaths.LogFile);
            if (!info.Exists || info.Length <= MaxBytes) return;
            File.Move(AppPaths.LogFile, AppPaths.OldLogFile, overwrite: true);
        }
        catch
        {
            // Sem log não há onde reclamar; o app segue.
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogsFolder);
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // idem
        }
    }
}
