using System;
using System.Security.Cryptography;

namespace MixerDeAudio.Remote;

/// <summary>Arquivo estático servido pelo celular: nome do recurso embutido, tipo e cache.</summary>
public sealed record WebAsset(string Resource, string ContentType, string CacheControl);

/// <summary>
/// Fase 14, D2 a D8: regras puras das rotas /app/* e /fontes/* e da página antiga em partes.
/// </summary>
public static class WebAssets
{
    public const string ResourcePrefix = "MixerDeAudio.Web/";
    public const string OldPagePrefix = "MixerDeAudio.Web/antigo/";
    public const string NoCache = "no-cache";
    public const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>Partes da página antiga, na ordem em que são concatenadas (D7).</summary>
    public static readonly string[] OldPageParts =
    {
        "01-inicio.html", "02-estilo.css", "03-corpo.html", "04-base.js", "05-conexao.js", "06-pareamento.js",
        "07-sliders.js", "08-som.js", "09-pc.js", "10-menu.js", "11-brilho.js", "12-estado-pc.js",
        "13-enviar.js", "14-sono.js", "15-janelas.js", "16-controle.js", "17-tela.js", "18-partida.js",
    };

    /// <summary>Tipo (D4) pela extensão; nulo se a extensão não é servida.</summary>
    public static string? ContentTypeFor(string path)
    {
        var dot = path.LastIndexOf('.');
        if (dot < 0) return null;
        return path.Substring(dot) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".webmanifest" => "application/manifest+json",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".woff2" => "font/woff2",
            ".txt" => "text/plain; charset=utf-8",
            _ => null,
        };
    }

    /// <summary>D2: resolve /app/... e /fontes/...; nulo se o caminho não é aceito.</summary>
    public static WebAsset? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string cache;
        if (path.StartsWith("/app/", StringComparison.Ordinal)) cache = NoCache;
        else if (path.StartsWith("/fontes/", StringComparison.Ordinal)) cache = Immutable;
        else return null;

        var rest = path.Substring(path.IndexOf('/', 1) + 1);
        if (rest.Length == 0 || rest.EndsWith('/')) return null;
        if (path.Contains("//", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)) return null;
        foreach (var c in rest)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '/' || c == '-';
            if (!ok) return null;
        }
        var type = ContentTypeFor(rest);
        if (type is null) return null;
        return new WebAsset(ResourcePrefix + path.Substring(1), type, cache);
    }

    /// <summary>D5: 16 hex do SHA-256 do conteúdo, entre aspas.</summary>
    public static string ETagFor(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return "\"" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + "\"";
    }

    /// <summary>D6: trata lista, validador fraco (W/) e "*".</summary>
    public static bool IfNoneMatchMatches(string? header, string etag)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        foreach (var raw in header.Split(','))
        {
            var t = raw.Trim();
            if (t == "*") return true;
            if (t.StartsWith("W/", StringComparison.Ordinal)) t = t.Substring(2);
            if (t == etag) return true;
        }
        return false;
    }
}
