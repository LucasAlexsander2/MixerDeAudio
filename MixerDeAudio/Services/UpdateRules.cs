using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MixerDeAudio.Services;

/// <summary>Um item do cartão "Novidades" (Fase 11, D8): ícone (código hexadecimal da Segoe Fluent), título e texto.</summary>
public sealed record NoteItem(string Icone, string Titulo, string Texto);

/// <summary>Conteúdo da faixa da janelinha (Fase 11, D6): se aparece, o texto e o botão (nulo = sem botão).</summary>
public sealed record BannerInfo(bool Visible, string Text, string? Button);

/// <summary>As novidades de uma versão, como estão em <c>Novidades.json</c>.</summary>
public sealed record ReleaseNotes(string Versao, List<NoteItem> Itens);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<ReleaseNotes>))]
internal sealed partial class ReleaseNotesContext : JsonSerializerContext
{
}

/// <summary>Regras puras da atualização (Fase 11, D4, D5 e D8): quando conferir, balão, versões e novidades.</summary>
public static class UpdateRules
{
    /// <summary>Intervalo entre as conferências automáticas (D5).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Espera da primeira conferência depois de abrir o app (D5).</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);

    /// <summary>Conferência automática: opção ligada e (nunca conferiu ou já passaram 6 h ou mais).</summary>
    public static bool ShouldCheck(DateTime now, DateTime? lastCheck, bool autoEnabled) =>
        autoEnabled && (lastCheck is null || now - lastCheck.Value >= Interval);

    /// <summary>Balão da bandeja: uma vez por versão (D6). <paramref name="lastBalloonVersion"/> é a última que já avisou.</summary>
    public static bool ShouldNotify(string? lastBalloonVersion, string? newVersion) =>
        !string.IsNullOrWhiteSpace(newVersion) &&
        !string.Equals(lastBalloonVersion?.Trim(), newVersion.Trim(), StringComparison.OrdinalIgnoreCase);


    /// <summary>Texto do estado na seção Atualização (D6).</summary>
    public static string StatusText(UpdateStatus s, bool checkedBefore)
    {
        var text = s.State switch
        {
            UpdateState.Unavailable => "Atualização indisponível nesta cópia (não instalada).",
            UpdateState.Idle => checkedBefore ? "Você está com a versão mais recente." : "Ainda não procurou atualizações.",
            UpdateState.Checking => "Procurando atualizações...",
            UpdateState.NewVersion => $"Versão {s.NewVersion} disponível.",
            UpdateState.Downloading => s.SecondsLeft is double left
                ? $"Baixando a versão {s.NewVersion}: {s.Percent}% ({DownloadEstimate.Format(left)})"
                : $"Baixando a versão {s.NewVersion}: {s.Percent}%",
            UpdateState.Ready => $"Versão {s.NewVersion} baixada. Reinicie para atualizar.",
            UpdateState.Error => $"Não foi possível procurar atualizações: {s.Error}",
            _ => "",
        };
        return s.State is not UpdateState.Error && !string.IsNullOrEmpty(s.Error) ? $"{text} ({s.Error})" : text;
    }

    /// <summary>Faixa no topo da janelinha (D6): visível só com versão nova, baixando ou pronta.</summary>
    public static BannerInfo Banner(UpdateStatus s) => s.State switch
    {
        UpdateState.NewVersion => new BannerInfo(true, $"Versão {s.NewVersion} disponível", "Atualizar"),
        UpdateState.Downloading => new BannerInfo(true, $"Baixando a versão {s.NewVersion}: {s.Percent}%", null),
        UpdateState.Ready => new BannerInfo(true, $"Versão {s.NewVersion} pronta", "Reiniciar"),
        _ => new BannerInfo(false, "", null),
    };

    /// <summary>Mensagem de erro em uma linha, com no máximo 80 caracteres.</summary>
    public static string Summarize(string? message)
    {
        var m = string.Join(' ', (message ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (m.Length == 0) m = "erro desconhecido";
        return m.Length <= 80 ? m : m[..77] + "...";
    }

    /// <summary>
    /// Compara versões numéricas pontuadas (1.1.0 &lt; 1.1.1 &lt; 1.10.0). Sufixos depois de '-' ou '+' são ignorados;
    /// partes que faltam valem 0; texto inválido vale 0.
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }

    private static int[] Parts(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return [];
        var t = v.Trim().TrimStart('v', 'V');
        var cut = t.IndexOfAny(['-', '+']);
        if (cut >= 0) t = t[..cut];
        return t.Split('.').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
    }

    /// <summary>Lê o <c>Novidades.json</c>. Nulo, vazio ou inválido devolve lista vazia.</summary>
    public static List<ReleaseNotes> ParseNotes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var list = JsonSerializer.Deserialize(json, ReleaseNotesContext.Default.ListReleaseNotes);
            return list?.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Versao) && r.Itens is not null).ToList() ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>As novidades da versão pedida, ou nulo se não houver entrada.</summary>
    public static ReleaseNotes? NotesFor(IEnumerable<ReleaseNotes> notes, string? version) =>
        string.IsNullOrWhiteSpace(version) ? null : notes.FirstOrDefault(r => CompareVersions(r.Versao, version) == 0);

    /// <summary>
    /// O cartão aparece uma vez (D8): quando a versão atual é maior que a última vista. Sem versão vista, só aparece se já
    /// havia configuração (veio de uma versão anterior); instalação nova (sem config) não mostra.
    /// </summary>
    public static bool ShouldShowNotes(string? lastSeenVersion, string? currentVersion, bool configExisted)
    {
        if (string.IsNullOrWhiteSpace(currentVersion)) return false;
        if (string.IsNullOrWhiteSpace(lastSeenVersion)) return configExisted;
        return CompareVersions(currentVersion, lastSeenVersion) > 0;
    }
}

/// <summary>Estima o tempo restante de um download só pela porcentagem (o Velopack não informa bytes).</summary>
public sealed class DownloadEstimate
{
    private const double Alpha = 0.3;
    private double? _smoothed;

    /// <summary>Segundos restantes, ou nulo enquanto faltam dados (menos de 3% ou menos de 1 s).</summary>
    public double? Register(int percent, TimeSpan elapsed)
    {
        if (percent < 3 || elapsed < TimeSpan.FromSeconds(1)) return null;
        var p = Math.Min(percent, 100);
        var raw = elapsed.TotalSeconds * (100 - p) / p;
        _smoothed = _smoothed is double previous ? Alpha * raw + (1 - Alpha) * previous : raw;
        return _smoothed;
    }

    /// <summary>"cerca de 8 s", "cerca de 1 min 05 s" ou "menos de 1 s".</summary>
    public static string Format(double seconds)
    {
        if (seconds < 1) return "menos de 1 s";
        var total = (int)Math.Round(seconds);
        if (total < 60) return $"cerca de {total} s";
        return string.Create(CultureInfo.InvariantCulture, $"cerca de {total / 60} min {total % 60:00} s");
    }
}
