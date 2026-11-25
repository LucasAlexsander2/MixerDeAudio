using System;

namespace MixerDeAudio.Pc;

/// <summary>Regras puras do brilho do monitor (Fase 12, D5 e D10): porcentagem de 0 a 100 ↔ faixa do monitor.</summary>
public static class BrightnessRules
{
    /// <summary>Corta a porcentagem em 0 a 100.</summary>
    public static int ClampPercent(int pct) => Math.Clamp(pct, 0, 100);

    /// <summary>Porcentagem → valor do monitor: <c>min + round(p × (max − min) / 100)</c>. Faixa de um valor só devolve <paramref name="min"/>.</summary>
    public static int ToRaw(int pct, int min, int max)
    {
        if (max <= min) return min;
        return min + (int)Math.Round(ClampPercent(pct) * (long)(max - min) / 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Valor do monitor → porcentagem (0 a 100). Faixa de um valor só devolve 100.</summary>
    public static int ToPercent(int raw, int min, int max)
    {
        if (max <= min) return 100;
        var r = Math.Clamp(raw, min, max);
        return ClampPercent((int)Math.Round((r - min) * 100.0 / (max - min), MidpointRounding.AwayFromZero));
    }
}
