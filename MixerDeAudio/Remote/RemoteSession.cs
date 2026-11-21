using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Audio;
using MixerDeAudio.Services;

namespace MixerDeAudio.Remote;

/// <summary>
/// Uma conexão WebSocket de um celular (Fase 6, D8 a D10). Recebe comandos (rede, pool de threads) e os enfileira na
/// thread da tela, onde o <see cref="AudioHub"/> os executa. O estado é montado na thread da tela, juntando as mudanças
/// em no máximo uma mensagem a cada 50 ms, e enviado de forma assíncrona por uma fila que guarda só o mais recente.
/// Cada sessão é um dono do hub (<c>celular:n</c>), de modo que o áudio é observado enquanto há celular conectado.
/// </summary>
internal sealed class RemoteSession
{
    private static readonly TimeSpan CoalesceInterval = TimeSpan.FromMilliseconds(50);

    private readonly RemoteServer _server;
    private readonly AudioHub _hub;
    private readonly WebSocket _socket;
    private readonly DispatcherQueue _ui;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private readonly string _owner;
    private DispatcherQueueTimer? _timer; // só na thread da tela
    private bool _dirty;                  // só na thread da tela
    private bool _attached;               // só na thread da tela
    private int _closing;

    public RemoteSession(RemoteServer server, AudioHub hub, WebSocket socket, DispatcherQueue ui, int number)
    {
        _server = server;
        _hub = hub;
        _socket = socket;
        _ui = ui;
        _owner = $"celular:{number}";
    }

    /// <summary>Hash da chave que autenticou esta conexão (para "Gerar novo código" e para o último uso).</summary>
    public string? KeyHash { get; init; }

    /// <summary>Pede o fim da conexão (de qualquer thread).</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0) return;
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Roda a conexão até ela terminar. Quem chama já contou a sessão no servidor.</summary>
    public async Task RunAsync()
    {
        var attached = await _server.OnUi(Attach).ConfigureAwait(false);
        if (!attached)
        {
            await CloseSocketAsync(WebSocketCloseStatus.EndpointUnavailable, "indisponivel").ConfigureAwait(false);
            return;
        }
        try
        {
            var receive = ReceiveLoopAsync(_cts.Token);
            var send = SendLoopAsync(_cts.Token);
            await Task.WhenAny(receive, send).ConfigureAwait(false);
            Close();
            try { await Task.WhenAll(receive, send).ConfigureAwait(false); }
            catch { /* já tratado dentro dos laços */ }
        }
        finally
        {
            try { await _server.OnUi(Detach).ConfigureAwait(false); }
            catch (InvalidOperationException) { /* o app está saindo: a fila da tela já foi encerrada */ }
            await CloseSocketAsync(WebSocketCloseStatus.NormalClosure, "fim").ConfigureAwait(false);
            _socket.Dispose();
            _cts.Dispose();
        }
    }

    // ---- Thread da tela: ligar e desligar do hub ----

    private bool Attach()
    {
        if (_attached) return true;
        _hub.Acquire(_owner);
        try
        {
            _hub.Sessions.Changed += OnSessionsChanged;
            _hub.Devices.Changed += OnDevicesChanged;
            _hub.MasterChanged += OnMasterChanged;
            _hub.RouteChanged += OnRouteChanged;
            App.Settings.Changed += OnSettingsChanged;
            _timer = _ui.CreateTimer();
            _timer.Interval = CoalesceInterval;
            _timer.IsRepeating = false;
            _timer.Tick += OnTimerTick;
            _attached = true;
            PushState(); // o estado inteiro ao conectar
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: {ex.Message}");
            Detach();
            return false;
        }
    }

    private bool Detach()
    {
        if (_attached)
        {
            _attached = false;
            if (_hub.IsActive) // ao sair do app o hub já pode ter soltado tudo
            {
                _hub.Sessions.Changed -= OnSessionsChanged;
                _hub.Devices.Changed -= OnDevicesChanged;
            }
            _hub.MasterChanged -= OnMasterChanged;
            _hub.RouteChanged -= OnRouteChanged;
            App.Settings.Changed -= OnSettingsChanged;
            if (_timer is not null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
        }
        _hub.Release(_owner);
        return true;
    }

    private void OnSessionsChanged(GroupChange change) => MarkDirty();
    private void OnDevicesChanged(DeviceChange change) => MarkDirty();
    private void OnMasterChanged(bool available, float volume, bool muted) => MarkDirty();
    private void OnRouteChanged(string key) => MarkDirty();
    private void OnSettingsChanged() => MarkDirty();

    /// <summary>Várias mudanças seguidas viram uma mensagem só, no máximo uma a cada 50 ms.</summary>
    private void MarkDirty()
    {
        if (!_attached || _dirty) return;
        _dirty = true;
        _timer?.Start();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        _dirty = false;
        PushState();
    }

    private void PushState()
    {
        if (!_attached) return;
        try
        {
            var json = RemoteProtocol.BuildState(BuildState(_hub, App.Settings.Current.Favoritos));
            _outbox.Writer.TryWrite(json);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO celular: {ex.Message}");
        }
    }

    /// <summary>Monta o estado do D8 a partir do hub, com as mesmas regras de saídas e botões da janela (Fase 4 e 5). Thread da tela.</summary>
    internal static RemoteState BuildState(AudioHub hub, IReadOnlyCollection<string> favorites)
    {
        var tracker = hub.Devices;
        var devices = tracker.Devices;
        var outputs = new List<RemoteOutput>();
        foreach (var d in FavoriteFilter.Visible(devices, favorites, tracker.ActiveId))
            outputs.Add(new RemoteOutput(d.Id, d.ShortName, DeviceGlyph.For(d.FormFactor), tracker.IsActive(d.Id)));

        var ids = new List<string>(devices.Count);
        foreach (var d in devices) ids.Add(d.Id);

        var apps = new List<RemoteApp>();
        foreach (var g in hub.Sessions.Groups)
        {
            var routes = new List<RemoteRoute>();
            var effective = AppRouting.Effective(hub.PreferredOf(g.Key), tracker.DefaultId, ids);
            var visible = FavoriteFilter.Visible(devices, favorites, effective);
            if (AppRouting.ShowButtons(g.IsSystem, hub.IsRouteLoaded(g.Key), visible.Count, hub.Routing is not null))
            {
                foreach (var d in visible)
                    routes.Add(new RemoteRoute(d.Id, DeviceGlyph.For(d.FormFactor), d.ShortName,
                        string.Equals(d.Id, effective, StringComparison.OrdinalIgnoreCase)));
            }
            apps.Add(new RemoteApp(RemoteProtocol.IconId(g.Key), g.Key, g.Name, (int)Math.Round(Math.Clamp(g.Volume, 0f, 1f) * 100.0),
                g.Muted, g.IsSystem, routes));
        }

        return new RemoteState(outputs, hub.MasterAvailable, (int)Math.Round(Math.Clamp(hub.MasterVolume, 0f, 1f) * 100.0),
            hub.MasterMuted, apps);
    }

    // ---- Comandos (D8): validar e executar na thread da tela ----

    private void Execute(RemoteCommand command)
    {
        if (!_attached) return;
        switch (command)
        {
            case VolumeCommand v when HasApp(v.App):
                _hub.SetVolume(v.App, v.Volume / 100f);
                break;
            case MuteCommand m when HasApp(m.App):
                _hub.SetMute(m.App, m.Muted);
                break;
            case MasterVolumeCommand gv:
                _hub.SetMasterVolume(gv.Volume / 100f);
                break;
            case MasterMuteCommand gm:
                _hub.SetMasterMute(gm.Muted);
                break;
            case DefaultDeviceCommand p when HasDevice(p.DeviceId):
                _hub.SetDefaultDevice(p.DeviceId);
                break;
            case RouteCommand r when HasApp(r.App) && HasDevice(r.DeviceId):
                _hub.ClickRoute(r.App, r.DeviceId);
                break;
            // chave ou id desconhecido: ignorado (D8)
        }
    }

    private bool HasApp(string key) => _hub.Sessions.SessionIdsOf(key).Count > 0;

    private bool HasDevice(string id)
    {
        foreach (var d in _hub.Devices.Devices)
            if (string.Equals(d.Id, id, StringComparison.Ordinal)) return true;
        return false;
    }

    // ---- Rede (pool de threads) ----

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[RemoteProtocol.MaxMessageBytes + 1];
        try
        {
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    length += result.Count;
                    if (length > RemoteProtocol.MaxMessageBytes)
                    {
                        // Mensagem acima de 4 KB: fecha a conexão (D8)
                        await CloseSocketAsync(WebSocketCloseStatus.MessageTooBig, "grande demais").ConfigureAwait(false);
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text) continue;
                var text = Encoding.UTF8.GetString(buffer, 0, length);
                var status = RemoteProtocol.ParseCommand(text, out var command);
                if (status == ParseStatus.TooLarge) return;
                if (status != ParseStatus.Ok || command is null) continue; // JSON inválido ou tipo desconhecido: ignorado
                var cmd = command;
                _ui.TryEnqueue(() =>
                {
                    try { Execute(cmd); }
                    catch (Exception ex) { Log.Write($"ERRO celular: {ex.Message}"); }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException) { }
        catch (Exception ex) { Log.Write($"ERRO celular: {ex.Message}"); }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _outbox.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_outbox.Reader.TryRead(out var json))
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex) when (ex is System.IO.IOException or ObjectDisposedException) { }
        catch (Exception ex) { Log.Write($"ERRO celular: {ex.Message}"); }
    }

    private async Task CloseSocketAsync(WebSocketCloseStatus status, string reason)
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(status, reason, timeout.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // a conexão já caiu
        }
    }
}
