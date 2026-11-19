using System;
using System.Collections.Generic;

namespace MixerDeAudio.Audio;

/// <summary>
/// Filtro do eco do próprio ajuste (D14). O Windows devolve ao app os valores que ele mesmo gravou; num arraste
/// rápido, um eco atrasado faria o slider pular para trás. Guarda por grupo os últimos 8 valores gravados no
/// último 1 s; um valor recebido a até 0,005 de um deles é eco. Lógica pura, relógio injetável.
/// </summary>
public sealed class VolumeEchoFilter
{
    public const int MaxValues = 8;
    public const double Tolerance = 0.005;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, List<(DateTime At, float Value)>> _written = new();

    public VolumeEchoFilter(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>Anota um valor gravado pelo app no grupo.</summary>
    public void Register(string key, float value)
    {
        if (!_written.TryGetValue(key, out var list)) _written[key] = list = new();
        list.Add((_now(), value));
        if (list.Count > MaxValues) list.RemoveRange(0, list.Count - MaxValues);
    }

    /// <summary>Se o valor recebido é eco de algo que o app gravou há menos de 1 s.</summary>
    public bool IsEcho(string key, float value)
    {
        if (!_written.TryGetValue(key, out var list)) return false;
        var limit = _now() - Window;
        list.RemoveAll(e => e.At < limit);
        foreach (var e in list)
            if (Math.Abs(e.Value - value) <= Tolerance) return true;
        return false;
    }
}
