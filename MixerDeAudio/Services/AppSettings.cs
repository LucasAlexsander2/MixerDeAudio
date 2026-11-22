using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Services;

/// <summary>Temas aceitos na configuração (D1).</summary>
public static class AppTheme
{
    public const string Auto = "automatico";
    public const string Light = "claro";
    public const string Dark = "escuro";

    /// <summary>Tema desconhecido, nulo ou vazio vira automático.</summary>
    public static string Normalize(string? theme)
    {
        var t = theme?.Trim().ToLowerInvariant();
        return t is Light or Dark ? t : Auto;
    }
}

/// <summary>
/// Configuração do usuário (D1), gravada em <c>config.json</c>. Lógica pura: lê e grava texto JSON, sem arquivo.
/// "Iniciar com o Windows" não fica aqui: a fonte da verdade é o registro (D3).
/// </summary>
public sealed record AppSettings
{
    public const int CurrentVersion = 1;
    public const int MaxFavorites = 3;
    public const int MaxPhones = 8;

    public int Versao { get; init; } = CurrentVersion;

    /// <summary>Ids (<c>MMDevice.ID</c>) das saídas favoritas, no máximo <see cref="MaxFavorites"/>.</summary>
    public List<string> Favoritos { get; init; } = new();

    /// <summary>Id -> último nome curto visto, para mostrar uma favorita que está desligada.</summary>
    public Dictionary<string, string> NomesFavoritos { get; init; } = new();

    public string Tema { get; init; } = AppTheme.Auto;

    /// <summary>Controle pelo celular ligado (Fase 6, D11); desligado por padrão.</summary>
    public bool CelularLigado { get; init; }

    /// <summary>Código de 6 dígitos do pareamento; gerado na primeira vez que o recurso é ligado.</summary>
    public string? CodigoCelular { get; init; }

    /// <summary>Celulares pareados: só o SHA-256 da chave fica aqui, nunca a chave. No máximo <see cref="MaxPhones"/>.</summary>
    public List<PairedPhone> ChavesCelular { get; init; } = new();

    /// <summary>Grupos de ferramentas do PC ligados (Fase 9, D1): ids de <c>RemoteTools</c>. Vazia = tudo desligado.</summary>
    public List<string> FerramentasCelular { get; init; } = new();

    /// <summary>Porta do servidor do celular (Fase 10, D12): 8750 a 8759; fora da faixa ou ausente vira 8750.</summary>
    public int PortaCelular { get; init; } = RemoteRules.FirstPort;

    /// <summary>Procurar atualizações automaticamente (Fase 11, D5); ligada por padrão. Só procura, nunca baixa sozinho.</summary>
    public bool AtualizarAuto { get; init; } = true;

    /// <summary>Última versão cujo cartão de novidades já foi mostrado (Fase 11, D8).</summary>
    public string? UltimaVersaoVista { get; init; }

    /// <summary>Última versão nova que já avisou por balão (Fase 11, D6): o balão sai uma vez por versão.</summary>
    public string? BalaoVersao { get; init; }

    public static AppSettings Default => new();

    /// <summary>Lê o JSON. Nulo, vazio, inválido ou de outra versão volta ao padrão (D1).</summary>
    public static AppSettings Parse(string? json) => Parse(json, out _);

    /// <summary><paramref name="invalid"/> é verdadeiro quando havia texto, mas não deu para aproveitar (para o log, D2).</summary>
    public static AppSettings Parse(string? json, out bool invalid)
    {
        invalid = false;
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            var read = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings);
            if (read is null || read.Versao != CurrentVersion)
            {
                invalid = true;
                return Default;
            }
            return read.Normalized();
        }
        catch (JsonException)
        {
            invalid = true;
            return Default;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(Normalized(), AppSettingsContext.Default.AppSettings);

    /// <summary>Tema válido, favoritos sem vazios nem repetidos e no máximo 3, nomes só dos favoritos.</summary>
    public AppSettings Normalized()
    {
        var favs = new List<string>();
        foreach (var id in Favoritos ?? new List<string>())
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (favs.Exists(f => string.Equals(f, id, StringComparison.Ordinal))) continue;
            if (favs.Count >= MaxFavorites) break;
            favs.Add(id);
        }
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (NomesFavoritos is not null)
        {
            foreach (var id in favs)
                if (NomesFavoritos.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)) names[id] = n;
        }
        return new AppSettings
        {
            Versao = CurrentVersion, Favoritos = favs, NomesFavoritos = names, Tema = AppTheme.Normalize(Tema),
            CelularLigado = CelularLigado, CodigoCelular = NormalizeCode(CodigoCelular), ChavesCelular = NormalizePhones(ChavesCelular),
            FerramentasCelular = RemoteTools.Normalize(FerramentasCelular),
            PortaCelular = RemoteRules.NormalizePort(PortaCelular),
            AtualizarAuto = AtualizarAuto, UltimaVersaoVista = NormalizeVersion(UltimaVersaoVista), BalaoVersao = NormalizeVersion(BalaoVersao),
        };
    }

    private static string? NormalizeVersion(string? version)
    {
        var v = version?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static string? NormalizeCode(string? code)
    {
        var c = code?.Trim();
        if (c is null || c.Length != 6) return null;
        foreach (var ch in c)
            if (ch is < '0' or > '9') return null;
        return c;
    }

    /// <summary>Sem hash vazio nem repetido (fica o de uso mais recente); acima de 8, saem os de uso mais antigo.</summary>
    private static List<PairedPhone> NormalizePhones(List<PairedPhone>? phones)
    {
        var valid = new List<PairedPhone>();
        foreach (var p in phones ?? new List<PairedPhone>())
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Hash)) continue;
            var at = valid.FindIndex(v => string.Equals(v.Hash, p.Hash, StringComparison.Ordinal));
            if (at < 0) valid.Add(p with { Nome = p.Nome ?? "" });
            else if (p.UltimoUso > valid[at].UltimoUso) valid[at] = p with { Nome = p.Nome ?? "" };
        }
        if (valid.Count <= MaxPhones) return valid;
        var keep = new HashSet<PairedPhone>(valid.OrderByDescending(p => p.UltimoUso).Take(MaxPhones));
        return valid.Where(keep.Contains).ToList();
    }
}

/// <summary>Um celular pareado (Fase 6, D11): hash da chave, nome tirado do User-Agent e o último uso.</summary>
public sealed record PairedPhone
{
    public string Hash { get; init; } = "";
    public string Nome { get; init; } = "";
    public DateTime UltimoUso { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsContext : JsonSerializerContext
{
}
