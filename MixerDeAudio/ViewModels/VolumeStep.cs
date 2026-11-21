using System;

namespace MixerDeAudio.ViewModels;

/// <summary>Roda do mouse nos sliders (Fase 8, D3): cada "dente" (delta 120) vale ±2, de 0 a 100. Lógica pura.</summary>
public static class VolumeStep
{
    /// <summary>Delta de um dente da roda.</summary>
    public const int NotchDelta = 120;

    /// <summary>Pontos por dente.</summary>
    public const int PointsPerNotch = 2;

    /// <summary>Novo valor (0 a 100) depois de girar a roda com o delta dado (positivo sobe). Delta parcial vale proporcional.</summary>
    public static double Apply(double value, int delta)
    {
        var step = Math.Round(delta / (double)NotchDelta * PointsPerNotch, MidpointRounding.AwayFromZero);
        return Math.Clamp(value + step, 0, 100);
    }
}
