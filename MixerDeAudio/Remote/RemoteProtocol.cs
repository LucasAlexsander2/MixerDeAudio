using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MixerDeAudio.Audio;
using MixerDeAudio.Pc;

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

/// <summary>Timer de energia em andamento (Fase 9, D8): ação, horário (epoch ms) e segundos que faltam.</summary>
public sealed record RemotePower(string Action, long AtMs, int RemainingSeconds);

/// <summary>Projeção atual (Fase 9, D9): modo (nulo se o Windows não disse) e quantas telas estão ligadas.</summary>
public sealed record RemoteProjection(string? Mode, int Screens);

/// <summary>Uma tela para "ver a tela" (Fase 9b, D18): índice, tamanho em pixels e se é a principal.</summary>
public sealed record RemoteScreen(int Index, int Width, int Height, bool Primary);

/// <summary>
/// O <c>pc</c> do estado (Fase 9, D6): só o que existe dos grupos ligados. <paramref name="PowerOn"/> é o grupo energia
/// (com <paramref name="Power"/> nulo = sem timer); <paramref name="Projection"/> não nulo = grupo monitores ligado;
/// <paramref name="InputOn"/> = grupo entrada ligado (com <paramref name="Awake"/> = "manter a tela ligada");
/// <paramref name="Screens"/> não nulo = grupo tela ligado.
/// </summary>
public sealed record RemotePcState(bool PowerOn = false, RemotePower? Power = null, RemoteProjection? Projection = null,
    bool InputOn = false, bool Awake = false, IReadOnlyList<RemoteScreen>? Screens = null,
    IReadOnlyList<string>? Shortcuts = null, bool InfoOn = false, RemoteSleep? Sleep = null);

/// <summary>Timer de sono em andamento (Fase 9c, D24): horário em que a música para (epoch ms) e segundos que faltam.</summary>
public sealed record RemoteSleep(long AtMs, int RemainingSeconds);

/// <summary>Tudo o que o celular mostra (D8). <paramref name="Tools"/>: grupos de ferramentas ligados; <paramref name="TestMode"/>: <c>--teste-pc</c>.</summary>
public sealed record RemoteState(IReadOnlyList<RemoteOutput> Outputs, bool MasterAvailable, int MasterVolume, bool MasterMuted, IReadOnlyList<RemoteApp> Apps,
    IReadOnlyCollection<string>? Tools = null, bool TestMode = false, RemotePcState? Pc = null);

/// <summary>Comando vindo do celular, já validado e com os valores cortados na faixa.</summary>
public abstract record RemoteCommand;
public sealed record VolumeCommand(string App, int Volume) : RemoteCommand;
public sealed record MuteCommand(string App, bool Muted) : RemoteCommand;
public sealed record MasterVolumeCommand(int Volume) : RemoteCommand;
public sealed record MasterMuteCommand(bool Muted) : RemoteCommand;
public sealed record DefaultDeviceCommand(string DeviceId) : RemoteCommand;
public sealed record RouteCommand(string App, string DeviceId) : RemoteCommand;
public sealed record MediaCommand(string App, MediaAction Action) : RemoteCommand;
/// <summary>Força um estado novo (a página manda ao abrir uma aba).</summary>
public sealed record RefreshCommand : RemoteCommand;
/// <summary>O celular pede para se desconectar.</summary>
public sealed record DisconnectCommand : RemoteCommand;
/// <summary>Energia (Fase 9, D8): <c>Seconds</c> 0 = agora, senão de 5 a 86400.</summary>
public sealed record PowerCommand(PowerAction Action, int Seconds) : RemoteCommand;
public sealed record PowerCancelCommand : RemoteCommand;
public sealed record ProjectionCommand(ProjectionMode Mode) : RemoteCommand;
/// <summary>Apagar (<c>Off</c> verdadeiro) ou acordar as telas.</summary>
public sealed record ScreensCommand(bool Off) : RemoteCommand;

/// <summary>Comandos do grupo entrada que rodam na thread de rede, sem passar pela fila da tela (Fase 9b, D13).</summary>
public abstract record InputCommand : RemoteCommand;
public sealed record MouseMoveCommand(int Dx, int Dy) : InputCommand;
public sealed record ClickCommand(MouseButton Button, bool Double) : InputCommand;
public sealed record ButtonCommand(MouseButton Button, bool Down) : InputCommand;
public sealed record ScrollCommand(int Dx, int Dy) : InputCommand;
public sealed record TextCommand(string Text) : InputCommand;
public sealed record KeyCommand(string Key) : InputCommand;
/// <summary><c>X</c> e <c>Y</c> de 0 a 1 dentro da tela <c>Monitor</c>.</summary>
public sealed record MouseAbsCommand(int Monitor, double X, double Y) : InputCommand;
/// <summary>"Manter a tela ligada" (D16): roda na thread da tela.</summary>
public sealed record AwakeCommand(bool On) : RemoteCommand;
/// <summary>Pede a lista de janelas (Fase 9c, D21).</summary>
public sealed record WindowsCommand : RemoteCommand;
/// <summary>Frente, minimizar ou fechar a janela de id <c>Id</c> (D22); o PC confere contra a lista antes de agir.</summary>
public sealed record WindowActionCommand(long Id, WindowAction Action) : RemoteCommand;
/// <summary>Abre o atalho de nome <c>Name</c> da pasta de atalhos (D23); só abre o que está na lista.</summary>
public sealed record ShortcutCommand(string Name) : RemoteCommand;
/// <summary>Timer de sono (D24): <c>Minutes</c> de 5 a 240; 0 em <c>Minutes</c> e <c>Seconds</c> cancela. <c>Seconds</c> (5 a 60) só vale no <c>--teste-pc</c>.</summary>
public sealed record SleepCommand(int Minutes, int Seconds = 0) : RemoteCommand;
/// <summary>Pede o texto da área de transferência do PC (Fase 9d, D25).</summary>
public sealed record ClipReadCommand : RemoteCommand;
/// <summary>Grava o texto na área de transferência do PC (D25); já cortado em 15.000 caracteres.</summary>
public sealed record ClipWriteCommand(string Text) : RemoteCommand;
/// <summary>Assina (ou cancela) o envio periódico de um grupo: <c>janelas</c> (D21) ou <c>info</c> (D27).</summary>
public sealed record SubscribeCommand(string Group, bool On) : RemoteCommand;

/// <summary>Resultado da leitura de uma mensagem do celular.</summary>
public enum ParseStatus
{
    Ok,
    /// <summary>Mais de 16 KB: a conexão deve ser fechada.</summary>
    TooLarge,
    InvalidJson,
    UnknownType,
    /// <summary>Tipo conhecido, mas faltam campos ou eles têm o tipo errado.</summary>
    InvalidFields,
}

/// <summary>Protocolo JSON do WebSocket (Fase 6, D8). Lógica pura.</summary>
public static class RemoteProtocol
{
    public const int MaxMessageBytes = 16 * 1024;

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
            // Fase 9, D6: grupos de ferramentas, modo de teste e o que existir do PC
            w.WriteStartObject("ferr");
            foreach (var id in RemoteTools.All)
                w.WriteBoolean(id, RemoteTools.IsEnabled(state.Tools, id));
            w.WriteEndObject();
            w.WriteBoolean("teste", state.TestMode);
            if (state.Pc is { } pc)
            {
                w.WriteStartObject("pc");
                if (pc.PowerOn)
                {
                    if (pc.Power is { } pw)
                    {
                        w.WriteStartObject("energia");
                        w.WriteString("acao", pw.Action);
                        w.WriteNumber("em", pw.AtMs);
                        w.WriteNumber("rest", Math.Max(0, pw.RemainingSeconds));
                        w.WriteEndObject();
                    }
                    else
                    {
                        w.WriteNull("energia");
                    }
                }
                if (pc.Projection is { } pj)
                {
                    w.WriteStartObject("projecao");
                    if (pj.Mode is null) w.WriteNull("modo"); else w.WriteString("modo", pj.Mode);
                    w.WriteNumber("telas", Math.Max(0, pj.Screens));
                    w.WriteEndObject();
                }
                if (pc.InputOn) w.WriteBoolean("acordado", pc.Awake);
                if (pc.Shortcuts is { } sh)
                {
                    w.WriteStartArray("atalhos");
                    foreach (var name in sh) w.WriteStringValue(name);
                    w.WriteEndArray();
                }
                if (pc.InfoOn)
                {
                    if (pc.Sleep is { } sl)
                    {
                        w.WriteStartObject("sono");
                        w.WriteNumber("em", sl.AtMs);
                        w.WriteNumber("rest", Math.Max(0, sl.RemainingSeconds));
                        w.WriteEndObject();
                    }
                    else
                    {
                        w.WriteNull("sono");
                    }
                }
                if (pc.Screens is { } scr)
                {
                    w.WriteStartArray("monitores");
                    foreach (var m in scr)
                    {
                        w.WriteStartObject();
                        w.WriteNumber("i", m.Index);
                        w.WriteNumber("w", m.Width);
                        w.WriteNumber("h", m.Height);
                        w.WriteBoolean("principal", m.Primary);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Outro celular está no controle (D3): <c>{"t":"ocupado","por":"Nome"}</c>.</summary>
    public static string BuildBusy(string name) => Simple("ocupado", "por", name);

    /// <summary>O PC (ou o próprio celular) desconectou (D4): <c>{"t":"desconectado"}</c>.</summary>
    public static string BuildDisconnected() => "{\"t\":\"desconectado\"}";

    /// <summary>Falha ou aviso para a página (D6): <c>{"t":"aviso","msg":"..."}</c>.</summary>
    public static string BuildNotice(string message) => Simple("aviso", "msg", message);

    /// <summary>Id do ícone de uma janela (Fase 9c, D21): o hash do caminho do <c>.exe</c>, que a rota <c>/icone</c> traduz de volta.</summary>
    public static string WindowIconId(string exePath) => IconId("janela:" + exePath.ToLowerInvariant());

    /// <summary>
    /// A lista de janelas (Fase 9c, D21): <c>{"t":"janelas","lista":[{"id","titulo","proc","icone","min"}]}</c>. Sem o caminho do
    /// <c>.exe</c>: só o id do ícone.
    /// </summary>
    public static string BuildWindows(IReadOnlyList<WindowItem> items)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("t", "janelas");
            w.WriteStartArray("lista");
            foreach (var it in items)
            {
                w.WriteStartObject();
                w.WriteNumber("id", it.Id);
                w.WriteString("titulo", WindowFilter.ClipTitle(it.Title));
                w.WriteString("proc", it.Process);
                w.WriteString("icone", string.IsNullOrEmpty(it.ExePath) ? "" : WindowIconId(it.ExePath));
                w.WriteBoolean("min", it.Minimized);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>O texto da área de transferência do PC (D25): <c>{"t":"clip","s":"..","cortado":bool}</c>.</summary>
    public static string BuildClip(string text, bool cut)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("t", "clip");
            w.WriteString("s", text);
            w.WriteBoolean("cortado", cut);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>
    /// O estado do PC (D27): <c>{"t":"info","cpu":N,"mem":{"usada","total"},"ligado":seg,"bateria":{"pct","carregando"}|null,
    /// "disco":{"livre","total"}}</c>.
    /// </summary>
    public static string BuildInfo(PcInfoSnapshot info)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("t", "info");
            w.WriteNumber("cpu", Math.Clamp(info.Cpu, 0, 100));
            w.WriteStartObject("mem");
            w.WriteNumber("usada", Math.Max(0, info.MemUsed));
            w.WriteNumber("total", Math.Max(0, info.MemTotal));
            w.WriteEndObject();
            w.WriteNumber("ligado", Math.Max(0, info.UptimeSeconds));
            if (info.Battery is { } pct)
            {
                w.WriteStartObject("bateria");
                w.WriteNumber("pct", pct);
                w.WriteBoolean("carregando", info.Charging);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("bateria");
            }
            w.WriteStartObject("disco");
            w.WriteNumber("livre", Math.Max(0, info.DiskFree));
            w.WriteNumber("total", Math.Max(0, info.DiskTotal));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static string Simple(string type, string field, string value)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("t", type);
            w.WriteString(field, value);
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
                case "atualizar":
                    command = new RefreshCommand();
                    return ParseStatus.Ok;
                case "desconectar":
                    command = new DisconnectCommand();
                    return ParseStatus.Ok;
                case "energia":
                    // acao e seg obrigatórios; seg fora da faixa (0 ou 5 a 86400) é recusado, nunca cortado
                    if (!Str(root, "acao", out var pacao) || !PowerRules.TryParse(pacao, out var pa)) return ParseStatus.InvalidFields;
                    if (!root.TryGetProperty("seg", out var segEl) || segEl.ValueKind != JsonValueKind.Number || !segEl.TryGetInt32(out var seg)) return ParseStatus.InvalidFields;
                    if (!PowerRules.IsValidSeconds(seg)) return ParseStatus.InvalidFields;
                    command = new PowerCommand(pa, seg);
                    return ParseStatus.Ok;
                case "energiaCancelar":
                    command = new PowerCancelCommand();
                    return ParseStatus.Ok;
                case "projecao":
                    if (!Str(root, "modo", out var pmodo) || !DisplayTopology.TryParseMode(pmodo, out var pm)) return ParseStatus.InvalidFields;
                    command = new ProjectionCommand(pm);
                    return ParseStatus.Ok;
                case "telas":
                    if (!Str(root, "acao", out var tacao)) return ParseStatus.InvalidFields;
                    if (tacao != "apagar" && tacao != "acordar") return ParseStatus.InvalidFields;
                    command = new ScreensCommand(tacao == "apagar");
                    return ParseStatus.Ok;
                case "mouse":
                    if (!Dbl(root, "dx", out var mdx) || !Dbl(root, "dy", out var mdy)) return ParseStatus.InvalidFields;
                    command = new MouseMoveCommand(InputRules.ClampDelta(mdx), InputRules.ClampDelta(mdy));
                    return ParseStatus.Ok;
                case "clique":
                    if (!Str(root, "b", out var cb) || !InputRules.TryParseButton(cb, out var cbtn)) return ParseStatus.InvalidFields;
                    var cdup = root.TryGetProperty("duplo", out _) && Bool(root, "duplo", out var cd) && cd;
                    command = new ClickCommand(cbtn, cdup);
                    return ParseStatus.Ok;
                case "botao":
                    if (!Str(root, "b", out var bb) || !InputRules.TryParseButton(bb, out var bbtn) || !Bool(root, "baixo", out var bdown)) return ParseStatus.InvalidFields;
                    command = new ButtonCommand(bbtn, bdown);
                    return ParseStatus.Ok;
                case "rolar":
                    var hasDx = Dbl(root, "dx", out var rdx);
                    var hasDy = Dbl(root, "dy", out var rdy);
                    if (!hasDx && !hasDy) return ParseStatus.InvalidFields;
                    command = new ScrollCommand(InputRules.ClampScroll(rdx), InputRules.ClampScroll(rdy));
                    return ParseStatus.Ok;
                case "texto":
                    if (!Str(root, "s", out var txt)) return ParseStatus.InvalidFields;
                    command = new TextCommand(InputRules.LimitText(txt));
                    return ParseStatus.Ok;
                case "tecla":
                    if (!Str(root, "k", out var key) || !InputRules.IsKnownKey(key)) return ParseStatus.InvalidFields;
                    command = new KeyCommand(key.ToLowerInvariant());
                    return ParseStatus.Ok;
                case "mouseAbs":
                    if (!Dbl(root, "mon", out var amon) || !Dbl(root, "x", out var ax) || !Dbl(root, "y", out var ay)) return ParseStatus.InvalidFields;
                    command = new MouseAbsCommand((int)Math.Clamp(Math.Round(amon), 0, 15), Math.Clamp(ax, 0.0, 1.0), Math.Clamp(ay, 0.0, 1.0));
                    return ParseStatus.Ok;
                case "acordado":
                    if (!Bool(root, "on", out var awake)) return ParseStatus.InvalidFields;
                    command = new AwakeCommand(awake);
                    return ParseStatus.Ok;
                case "janelas":
                    command = new WindowsCommand();
                    return ParseStatus.Ok;
                case "janela":
                    if (!Dbl(root, "id", out var wid) || wid != Math.Floor(wid) || Math.Abs(wid) > 9e15) return ParseStatus.InvalidFields;
                    if (!Str(root, "acao", out var wacao) || !WindowFilter.TryParseAction(wacao, out var wa)) return ParseStatus.InvalidFields;
                    command = new WindowActionCommand((long)wid, wa);
                    return ParseStatus.Ok;
                case "atalho":
                    if (!Str(root, "nome", out var sname)) return ParseStatus.InvalidFields;
                    command = new ShortcutCommand(sname);
                    return ParseStatus.Ok;
                case "sono":
                    // min: 0 (cancela) ou 5 a 240; seg (5 a 60) é só do modo de teste, quem executa confere. Fora da faixa é recusado.
                    var hasMin = root.TryGetProperty("min", out var minEl);
                    var hasSeg = root.TryGetProperty("seg", out var sEl);
                    if (!hasMin && !hasSeg) return ParseStatus.InvalidFields;
                    var smin = 0;
                    var sseg = 0;
                    if (hasMin && (minEl.ValueKind != JsonValueKind.Number || !minEl.TryGetInt32(out smin))) return ParseStatus.InvalidFields;
                    if (hasSeg && (sEl.ValueKind != JsonValueKind.Number || !sEl.TryGetInt32(out sseg))) return ParseStatus.InvalidFields;
                    if (hasMin && smin != 0 && !SleepRules.IsValidMinutes(smin)) return ParseStatus.InvalidFields;
                    if (hasSeg && !SleepRules.IsValidTestSeconds(sseg)) return ParseStatus.InvalidFields;
                    command = new SleepCommand(hasSeg ? 0 : smin, hasSeg ? sseg : 0);
                    return ParseStatus.Ok;
                case "clipLer":
                    command = new ClipReadCommand();
                    return ParseStatus.Ok;
                case "clipGravar":
                    // Texto vazio é válido (limpa a área de transferência); só precisa ser string
                    if (!root.TryGetProperty("s", out var cs) || cs.ValueKind != JsonValueKind.String) return ParseStatus.InvalidFields;
                    command = new ClipWriteCommand(ClipboardBridge.Limit(cs.GetString()).Text);
                    return ParseStatus.Ok;
                case "assinar":
                    if (!Str(root, "g", out var sgroup) || sgroup is not ("janelas" or "info") || !Bool(root, "on", out var son)) return ParseStatus.InvalidFields;
                    command = new SubscribeCommand(sgroup, son);
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

    /// <summary>Número qualquer (sem NaN nem infinito); a faixa é cortada por quem usa.</summary>
    private static bool Dbl(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Number) return false;
        if (!e.TryGetDouble(out var d) || double.IsNaN(d) || double.IsInfinity(d)) return false;
        value = d;
        return true;
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
