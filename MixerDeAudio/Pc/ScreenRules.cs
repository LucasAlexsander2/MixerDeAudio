using System;
using System.Collections.Generic;

namespace MixerDeAudio.Pc;

/// <summary>Uma tela ligada ao PC, em pixels da área de trabalho virtual (Fase 9b, D18).</summary>
public readonly record struct ScreenInfo(int Index, int Left, int Top, int Width, int Height, bool Primary);

/// <summary>Uma das qualidades da transmissão (Fase 10, D3): intervalo mínimo entre quadros, largura máxima e qualidade do JPEG.</summary>
public readonly record struct ScreenQuality(string Id, TimeSpan Interval, int MaxWidth, float Jpeg);

/// <summary>Regras puras de "ver a tela" (Fase 9b, D17 a D19; Fase 10, D3): qualidades, tamanho do quadro e ponto absoluto do mouse.</summary>
public static class ScreenRules
{
    public const int MaxWidth = 1920;
    public const int MinWidth = 160;
    public const int DefaultWidth = 640;
    /// <summary>Sem quadro por tanto tempo, a transmissão é considerada parada (só para o log).</summary>
    public static readonly TimeSpan IdleEnd = TimeSpan.FromSeconds(10);

    public static readonly ScreenQuality Eco = new("eco", TimeSpan.FromMilliseconds(500), 960, 0.5f);
    public static readonly ScreenQuality Normal = new("normal", TimeSpan.FromMilliseconds(125), 1280, 0.7f);
    public static readonly ScreenQuality Alta = new("alta", TimeSpan.FromMilliseconds(83), 1920, 0.8f);

    /// <summary>Qualidade pelo id ("eco", "normal", "alta"); nulo ou desconhecido vira "normal".</summary>
    public static ScreenQuality QualityFor(string? id) => id switch
    {
        "eco" => Eco,
        "alta" => Alta,
        _ => Normal,
    };

    /// <summary>
    /// Tamanho do quadro: largura pedida (0, negativa ou ausente = 640), entre 160 e o limite da qualidade
    /// (<paramref name="maxWidth"/>, no máximo 1920) e nunca maior que a da tela (sem ampliar); altura na mesma proporção.
    /// </summary>
    public static (int Width, int Height) FrameSize(int sourceWidth, int sourceHeight, int requestedWidth, int maxWidth = MaxWidth)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return (0, 0);
        var w = requestedWidth <= 0 ? DefaultWidth : requestedWidth;
        w = Math.Clamp(w, MinWidth, Math.Clamp(maxWidth, MinWidth, MaxWidth));
        if (w > sourceWidth) w = sourceWidth;
        var h = (int)Math.Round(sourceHeight * (double)w / sourceWidth, MidpointRounding.AwayFromZero);
        return (w, Math.Max(1, h));
    }

    /// <summary>Telas com a principal primeiro e as outras da esquerda para a direita; os índices são refeitos nessa ordem.</summary>
    public static List<ScreenInfo> Order(IEnumerable<ScreenInfo> raw)
    {
        var list = new List<ScreenInfo>(raw);
        list.Sort((a, b) =>
        {
            if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
            var c = a.Left.CompareTo(b.Left);
            return c != 0 ? c : a.Top.CompareTo(b.Top);
        });
        for (var i = 0; i < list.Count; i++) list[i] = list[i] with { Index = i };
        return list;
    }

    /// <summary>Retângulo da área de trabalho virtual (união das telas). Tudo zero se não há telas.</summary>
    public static (int Left, int Top, int Width, int Height) VirtualBounds(IReadOnlyList<ScreenInfo> screens)
    {
        if (screens.Count == 0) return (0, 0, 0, 0);
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var s in screens)
        {
            l = Math.Min(l, s.Left);
            t = Math.Min(t, s.Top);
            r = Math.Max(r, s.Left + s.Width);
            b = Math.Max(b, s.Top + s.Height);
        }
        return (l, t, r - l, b - t);
    }

    /// <summary>
    /// Ponto (0 a 1 dentro da tela <paramref name="monitor"/>) como coordenada absoluta de 0 a 65535 da área virtual
    /// (<c>MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK</c>). Nulo se a tela não existe.
    /// </summary>
    public static (int X, int Y)? AbsolutePoint(IReadOnlyList<ScreenInfo> screens, int monitor, double x, double y)
    {
        if (monitor < 0 || monitor >= screens.Count) return null;
        var s = screens[monitor];
        if (s.Width <= 0 || s.Height <= 0) return null;
        var v = VirtualBounds(screens);
        var fx = double.IsNaN(x) ? 0 : Math.Clamp(x, 0, 1);
        var fy = double.IsNaN(y) ? 0 : Math.Clamp(y, 0, 1);
        var px = s.Left + fx * (s.Width - 1);
        var py = s.Top + fy * (s.Height - 1);
        return (ToAbsolute(px - v.Left, v.Width), ToAbsolute(py - v.Top, v.Height));
    }

    /// <summary>Centro do pixel <paramref name="offset"/> numa área de <paramref name="size"/> pixels, na escala de 0 a 65535.</summary>
    private static int ToAbsolute(double offset, int size)
    {
        if (size <= 0) return 0;
        return (int)Math.Clamp(Math.Floor((offset + 0.5) * 65536.0 / size), 0, 65535);
    }
}
