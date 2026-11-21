using System;

namespace MixerDeAudio.Flyout;

/// <summary>
/// Altura da janelinha (Fase 8, D2): acompanha o conteúdo, entre um mínimo e um máximo. Lógica pura.
/// </summary>
public static class FlyoutSize
{
    /// <summary>Altura mínima, em DIP.</summary>
    public const double MinDip = 260;

    /// <summary>Altura máxima absoluta, em DIP.</summary>
    public const double MaxDip = 560;

    /// <summary>Fração da área útil do monitor que a janela pode ocupar.</summary>
    public const double WorkAreaFraction = 0.7;

    /// <summary>Largura em que o conteúdo é medido, em DIP.</summary>
    public const double WidthDip = 420;

    /// <summary>Maior altura permitida, em DIP: o menor entre <see cref="MaxDip"/> e 70% da área útil.</summary>
    public static double Max(double workAreaHeightPx, double scale)
    {
        if (scale <= 0) scale = 1;
        return Math.Min(MaxDip, WorkAreaFraction * workAreaHeightPx / scale);
    }

    /// <summary>
    /// Altura final em DIP: a desejada limitada entre <see cref="MinDip"/> e <see cref="Max"/>. Se a área útil for tão
    /// pequena que o máximo fique abaixo do mínimo, vale o máximo.
    /// </summary>
    /// <param name="desiredDip">Altura que o conteúdo pede, em DIP.</param>
    /// <param name="workAreaHeightPx">Altura da área útil do monitor, em pixels.</param>
    /// <param name="scale">Escala do monitor (DPI / 96).</param>
    public static double Height(double desiredDip, double workAreaHeightPx, double scale)
    {
        var max = Max(workAreaHeightPx, scale);
        return Math.Min(Math.Max(desiredDip, MinDip), max);
    }
}
