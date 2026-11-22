using System;

namespace MixerDeAudio.Pc;

/// <summary>Regras puras do timer de sono da música (Fase 9c, D24).</summary>
public static class SleepRules
{
    public const int MinMinutes = 5;
    public const int MaxMinutes = 240;

    /// <summary>Duração da descida do volume (últimos 30 s).</summary>
    public const int FadeSeconds = 30;

    /// <summary>Só no <c>--teste-pc</c>: o <c>seg</c> do comando (para o roteiro).</summary>
    public const int MinTestSeconds = 5;
    public const int MaxTestSeconds = 60;

    /// <summary>De 5 a 240 minutos (0 cancela e é tratado à parte).</summary>
    public static bool IsValidMinutes(int minutes) => minutes is >= MinMinutes and <= MaxMinutes;

    public static bool IsValidTestSeconds(int seconds) => seconds is >= MinTestSeconds and <= MaxTestSeconds;

    /// <summary>Quantos segundos dura a descida: 30, ou o tempo todo se o timer for mais curto que isso.</summary>
    public static int FadeFor(int totalSeconds) => Math.Clamp(totalSeconds, 0, FadeSeconds);

    /// <summary>
    /// Volume geral no passo <paramref name="step"/> de <paramref name="steps"/> (passos de 1 s): do valor original (passo 0) linearmente
    /// até 0 (último passo). Sempre entre 0 e o original.
    /// </summary>
    public static float Volume(float original, int step, int steps)
    {
        original = Math.Clamp(original, 0f, 1f);
        if (steps <= 0 || step >= steps) return 0f;
        if (step <= 0) return original;
        return original * (steps - step) / steps;
    }
}

/// <summary>Timer de sono em andamento: quando termina de fato (a descida começa <see cref="SleepRules.FadeFor"/> s antes).</summary>
public sealed record SleepPlan(DateTimeOffset DueAt, int TotalSeconds)
{
    public int FadeSeconds => SleepRules.FadeFor(TotalSeconds);

    /// <summary>Quando a descida do volume começa.</summary>
    public DateTimeOffset FadeStartsAt => DueAt - TimeSpan.FromSeconds(FadeSeconds);
}
