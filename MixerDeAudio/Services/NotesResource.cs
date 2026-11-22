using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MixerDeAudio.Services;

/// <summary>Lê o <c>Novidades.json</c> embutido no app (Fase 11, D8).</summary>
internal static class NotesResource
{
    public const string ResourceName = "MixerDeAudio.Novidades.json";

    public static List<ReleaseNotes> Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null) return new();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return UpdateRules.ParseNotes(reader.ReadToEnd());
        }
        catch
        {
            return new();
        }
    }
}
