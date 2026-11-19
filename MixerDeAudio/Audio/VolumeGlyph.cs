namespace MixerDeAudio.Audio;

/// <summary>Escolhe o glifo do alto-falante (Segoe Fluent Icons) pelo volume e pelo mudo (D15).</summary>
public static class VolumeGlyph
{
    public const string Muted = "";
    public const string Low = "";
    public const string Medium = "";
    public const string High = "";

    /// <param name="percent">Volume de 0 a 100.</param>
    public static string For(int percent, bool muted)
    {
        if (muted || percent <= 0) return Muted;
        if (percent <= 33) return Low;
        if (percent <= 66) return Medium;
        return High;
    }
}
