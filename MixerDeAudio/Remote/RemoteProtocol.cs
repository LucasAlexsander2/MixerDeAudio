using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MixerDeAudio.Audio;

namespace MixerDeAudio.Remote;

/// <summary>Botão de saída do topo (já filtrado pela Fase 5, D5).</summary>
public sealed record RemoteOutput(string Id, string Name, string Glyph, bool Active);

/// <summary>Botão de saída de um app.</summary>
public sealed record RemoteRoute(string Id, string Glyph, string Name, bool Active);

/// <summary>Mídia de um app (Fase 7, D9): faixa, estado e quais botões o app oferece.</summary>
public sealed record RemoteMedia(string Title, string Artist, bool Playing, bool CanPrev, bool CanPlayPause, bool CanNext);

/// <summary>Um app (uma linha da janelinha). <paramref name="MediaOnly"/>: linha só de mídia (sem volume, mudo nem rotas).</summary>
public sealed record RemoteApp(string IconId, string Key, string Name, int Volume, bool Muted, bool IsSystem, IReadOnlyList<RemoteRoute> Routes,
    RemoteMedia? Media = null, bool MediaOnly = false);

/// <summary>Tudo o que o celular mostra (D8).</summary>
public sealed record RemoteState(IReadOnlyList<RemoteOutput> Outputs, bool MasterAvailable, int MasterVolume, bool MasterMuted, IReadOnlyList<RemoteApp> Apps);

/// <summary>Comando vindo do celular, já validado e com os valores cortados na faixa.</summary>
public abstract record RemoteCommand;
public sealed record VolumeCommand(string App, int Volume) : RemoteCommand;
public sealed record MuteCommand(string App, bool Muted) : RemoteCommand;
public sealed record MasterVolumeCommand(int Volume) : RemoteCommand;
public sealed record MasterMuteCommand(bool Muted) : RemoteCommand;
public sealed record DefaultDeviceCommand(string DeviceId) : RemoteCommand;
public sealed record RouteCommand(string App, string DeviceId) : RemoteCommand;
public sealed record MediaCommand(string App, MediaAction Action) : RemoteCommand;

/// <summary>Resultado da leitura de uma mensagem do celular.</summary>
public enum ParseStatus
{
    Ok,
    /// <summary>Mais de 4 KB: a conexão deve ser fechada.</summary>
    TooLarge,
    InvalidJson,
    UnknownType,
    /// <summary>Tipo conhecido, mas faltam campos ou eles têm o tipo errado.</summary>
    InvalidFields,
}

/// <summary>Protocolo JSON do WebSocket (Fase 6, D8). Lógica pura.</summary>
public static class RemoteProtocol
{
    public const int MaxMessageBytes = 4 * 1024;

    /// <summary>Id do ícone: 16 caracteres hexadecimais do SHA-256 da chave do grupo (não expõe o caminho).</summary>
    public static string IconId(string groupKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(groupKey)), 0, 8).ToLowerInvariant();

    public static bool IsIconId(string? id)
    {
        if (id is null || id.Length != 16) return false;
        foreach (var c in id)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }

    /// <summary>Monta a mensagem <c>{"t":"estado",...}</c>.</summary>
    public static string BuildState(RemoteState state)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("t", "estado");
            w.WriteStartArray("saidas");
            foreach (var o in state.Outputs)
            {
                w.WriteStartObject();
                w.WriteString("id", o.Id);
                w.WriteString("nome", o.Name);
                w.WriteString("glifo", o.Glyph);
                w.WriteBoolean("ativa", o.Active);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("geral");
            w.WriteBoolean("disponivel", state.MasterAvailable);
            w.WriteNumber("volume", Math.Clamp(state.MasterVolume, 0, 100));
            w.WriteBoolean("mudo", state.MasterMuted);
            w.WriteEndObject();
            w.WriteStartArray("apps");
            foreach (var a in state.Apps)
            {
                w.WriteStartObject();
                w.WriteString("icone", a.IconId);
                w.WriteString("chave", a.Key);
                w.WriteString("nome", a.Name);
                w.WriteNumber("volume", Math.Clamp(a.Volume, 0, 100));
                w.WriteBoolean("mudo", a.Muted);
                w.WriteBoolean("sistema", a.IsSystem);
                w.WriteStartArray("rotas");
                foreach (var r in a.Routes)
                {
                    w.WriteStartObject();
                    w.WriteString("id", r.Id);
                    w.WriteString("glifo", r.Glyph);
                    w.WriteString("nome", r.Name);
                    w.WriteBoolean("ativa", r.Active);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteBoolean("soMidia", a.MediaOnly);
                if (a.Media is { } md)
                {
                    w.WriteStartObject("midia");
                    w.WriteString("titulo", md.Title);
                    w.WriteString("artista", md.Artist);
                    w.WriteBoolean("tocando", md.Playing);
                    w.WriteBoolean("ant", md.CanPrev);
                    w.WriteBoolean("pp", md.CanPlayPause);
                    w.WriteBoolean("prox", md.CanNext);
                    w.WriteEndObject();
                }
                else
                {
                    w.WriteNull("midia");
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// Lê um comando do celular. Valores numéricos fora da faixa são cortados em 0 a 100. A chave ou o id que não existem
    /// não são checados aqui: quem executa ignora o que não conhece.
    /// </summary>
    public static ParseStatus ParseCommand(string? json, out RemoteCommand? command)
    {
        command = null;
        if (json is null) return ParseStatus.InvalidJson;
        if (Encoding.UTF8.GetByteCount(json) > MaxMessageBytes) return ParseStatus.TooLarge;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ParseStatus.InvalidJson;
            if (!root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String) return ParseStatus.UnknownType;
            switch (t.GetString())
            {
                case "volume":
                    if (!Str(root, "app", out var app) || !Num(root, "v", out var v)) return ParseStatus.InvalidFields;
                    command = new VolumeCommand(app, v);
                    return ParseStatus.Ok;
                case "mudo":
                    if (!Str(root, "app", out var app2) || !Bool(root, "m", out var m)) return ParseStatus.InvalidFields;
                    command = new MuteCommand(app2, m);
                    return ParseStatus.Ok;
                case "geral":
                    if (!Num(root, "v", out var gv)) return ParseStatus.InvalidFields;
                    command = new MasterVolumeCommand(gv);
                    return ParseStatus.Ok;
                case "geralMudo":
                    if (!Bool(root, "m", out var gm)) return ParseStatus.InvalidFields;
                    command = new MasterMuteCommand(gm);
                    return ParseStatus.Ok;
                case "padrao":
                    if (!Str(root, "id", out var pid)) return ParseStatus.InvalidFields;
                    command = new DefaultDeviceCommand(pid);
                    return ParseStatus.Ok;
                case "rota":
                    if (!Str(root, "app", out var rapp) || !Str(root, "id", out var rid)) return ParseStatus.InvalidFields;
                    command = new RouteCommand(rapp, rid);
                    return ParseStatus.Ok;
                case "midia":
                    if (!Str(root, "app", out var mapp) || !Str(root, "acao", out var acao)) return ParseStatus.InvalidFields;
                    MediaAction? ma = acao switch { "ant" => MediaAction.Previous, "pp" => MediaAction.PlayPause, "prox" => MediaAction.Next, _ => null };
                    if (ma is null) return ParseStatus.InvalidFields;
                    command = new MediaCommand(mapp, ma.Value);
                    return ParseStatus.Ok;
                default:
                    return ParseStatus.UnknownType;
            }
        }
        catch (JsonException)
        {
            return ParseStatus.InvalidJson;
        }
    }

    private static bool Str(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.String) return false;
        var s = e.GetString();
        if (string.IsNullOrEmpty(s)) return false;
        value = s;
        return true;
    }

    private static bool Bool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var e)) return false;
        if (e.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (e.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }

    /// <summary>Número de 0 a 100: corta o que passar da faixa e arredonda os decimais.</summary>
    private static bool Num(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Number) return false;
        if (!e.TryGetDouble(out var d) || double.IsNaN(d)) return false;
        value = (int)Math.Round(Math.Clamp(d, 0.0, 100.0), MidpointRounding.AwayFromZero);
        return true;
    }
}
