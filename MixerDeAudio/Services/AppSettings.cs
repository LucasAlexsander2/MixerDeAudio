using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public int Versao { get; init; } = CurrentVersion;

    /// <summary>Ids (<c>MMDevice.ID</c>) das saídas favoritas, no máximo <see cref="MaxFavorites"/>.</summary>
    public List<string> Favoritos { get; init; } = new();

    /// <summary>Id -> último nome curto visto, para mostrar uma favorita que está desligada.</summary>
    public Dictionary<string, string> NomesFavoritos { get; init; } = new();

    public string Tema { get; init; } = AppTheme.Auto;

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
        return new AppSettings { Versao = CurrentVersion, Favoritos = favs, NomesFavoritos = names, Tema = AppTheme.Normalize(Tema) };
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsContext : JsonSerializerContext
{
}
