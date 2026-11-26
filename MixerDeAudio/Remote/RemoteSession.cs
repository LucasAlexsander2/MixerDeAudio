using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Audio;
using MixerDeAudio.Pc;
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
    /// <summary>Fase 9, D5: mensagens avulsas esperando para sair (além do estado). Passou disso, o celular está lento demais.</summary>
    internal const int MaxQueued = 64;

    // Fila de saída (D5): o estado guarda só o mais recente; o resto vai numa fila FIFO que nunca descarta.
    private readonly object _outLock = new();
    private readonly Queue<string> _queue = new();
    private string? _latestState;
    private (int Code, string Reason)? _finalClose;
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private readonly string _owner;
    private DispatcherQueueTimer? _timer; // só na thread da tela
    private bool _dirty;                  // só na thread da tela
    private bool _attached;               // só na thread da tela
    private bool _pcHooked;               // só na thread da tela: ouvindo App.Pc.Changed
    private int _closing;

    // Entrada (Fase 9b, D13 e D14): executada na thread de rede, sob este lock. _input é a cópia da configuração: nulo = grupo desligado.
    private readonly object _inLock = new();
    private IPcActions? _input;
    private int _held; // botões apertados (bit por MouseButton), soltos ao desconectar ou ao desligar o grupo

    // Janelas e atalhos (Fase 9c), só na thread da tela
    private static readonly TimeSpan WindowsInterval = TimeSpan.FromSeconds(3);
    private DispatcherQueueTimer? _winTimer;  // só existe com a aba Janelas assinada (D21)
    private string? _winSig;                  // a última lista enviada (só se mudou)
    private List<string>? _shortcuts;         // nulo = grupo atalhos desligado; lida ao conectar e a cada "atualizar" (D23)

    // Estado do PC (Fase 9d, D27): o timer e o leitor só existem com a aba PC assinada; só na thread da tela
    private static readonly TimeSpan InfoInterval = TimeSpan.FromSeconds(2);
    private DispatcherQueueTimer? _infoTimer;
    private PcInfoReader? _info;

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

    /// <summary>Nome do celular (tirado do User-Agent no pareamento), para a tela "ocupado" e as Configurações.</summary>
    public string PhoneName { get; init; } = "";

    /// <summary>Mensagem avulsa para o celular (de qualquer thread). Acima de <see cref="MaxQueued"/> a conexão é fechada.</summary>
    public void Post(string json)
    {
        bool overflow;
        lock (_outLock)
        {
            overflow = _queue.Count >= MaxQueued;
            if (!overflow) _queue.Enqueue(json);
        }
        if (overflow)
        {
            Log.Write("celular: fila de saida cheia (celular lento demais)");
            Close();
            return;
        }
        _signal.Writer.TryWrite(true);
    }

    private void PostState(string json)
    {
        lock (_outLock) _latestState = json;
        _signal.Writer.TryWrite(true);
    }

    /// <summary>
    /// Manda <paramref name="message"/> (se houver) e fecha a conexão com o código dado, depois de tudo o que já estava na fila
    /// (D4: 4000 = desconectado; 4001 = substituída pela mesma chave). De qualquer thread.
    /// </summary>
    public void Finish(string? message, int code, string reason)
    {
        lock (_outLock)
        {
            if (_finalClose is not null) return;
            if (message is not null) _queue.Enqueue(message);
            _finalClose = (code, reason);
        }
        _signal.Writer.TryWrite(true);
    }

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
            _hub.MediaChanged += OnMediaChanged;
            App.Settings.Changed += OnSettingsChanged;
            if (HasTools()) { App.Pc.Changed += OnPcChanged; App.Pc.BrightnessFailed += OnBrightnessFailed; }
            _pcHooked = HasTools();
            if (RemoteTools.IsEnabled(App.Settings.Current.FerramentasCelular, RemoteTools.Display)) App.Pc.RefreshProjection();
            SyncInput();
            RefreshShortcuts();
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
            _hub.MediaChanged -= OnMediaChanged;
            App.Settings.Changed -= OnSettingsChanged;
            if (_pcHooked) { App.Pc.Changed -= OnPcChanged; App.Pc.BrightnessFailed -= OnBrightnessFailed; }
            _pcHooked = false;
            DisableInput();
            StopWindows();
            StopInfo();
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
    private void OnMediaChanged() => MarkDirty();
    private void OnSettingsChanged()
    {
        // Ligou o primeiro grupo depois de conectar: passa a ouvir o controlador do PC
        if (_attached && !_pcHooked && HasTools())
        {
            App.Pc.Changed += OnPcChanged;
            App.Pc.BrightnessFailed += OnBrightnessFailed;
            _pcHooked = true;
        }
        if (_attached)
        {
            SyncInput();
            var s = App.Settings.Current;
            var shortcutsOn = s.CelularLigado && RemoteTools.IsEnabled(s.FerramentasCelular, RemoteTools.Shortcuts);
            if (shortcutsOn != (_shortcuts is not null)) RefreshShortcuts();
            if (!(s.CelularLigado && RemoteTools.IsEnabled(s.FerramentasCelular, RemoteTools.Windows))) StopWindows();
            if (!(s.CelularLigado && RemoteTools.IsEnabled(s.FerramentasCelular, RemoteTools.Info))) StopInfo();
        }
        MarkDirty();
    }

    /// <summary>Lê a pasta de atalhos (D23): ao conectar, a cada "atualizar" e ao ligar o grupo; nulo com o grupo desligado.</summary>
    private void RefreshShortcuts()
    {
        var s = App.Settings.Current;
        _shortcuts = s.CelularLigado && RemoteTools.IsEnabled(s.FerramentasCelular, RemoteTools.Shortcuts) ? App.Pc.ShortcutNames() : null;
    }

    // ---- Janelas (D21): a lista só é lida a pedido ou com a aba assinada, e só é enviada se mudou ----

    private void StartWindows()
    {
        if (_winTimer is null)
        {
            _winTimer = _ui.CreateTimer();
            _winTimer.Interval = WindowsInterval;
            _winTimer.IsRepeating = true;
            _winTimer.Tick += OnWindowsTick;
            _winTimer.Start();
        }
        SendWindows(force: true);
    }

    private void StopWindows()
    {
        _winSig = null;
        if (_winTimer is null) return;
        _winTimer.Stop();
        _winTimer.Tick -= OnWindowsTick;
        _winTimer = null;
    }

    private void OnWindowsTick(DispatcherQueueTimer sender, object args)
    {
        if (!_attached) return;
        SendWindows(force: false);
    }

    private void SendWindows(bool force)
    {
        var json = RemoteProtocol.BuildWindows(App.Pc.ListWindows());
        if (!force && json == _winSig) return;
        _winSig = json;
        Post(json);
    }

    // ---- Estado do PC (D27): um timer de 2 s, só com a aba PC aberta; para ao desassinar, ao desligar o grupo e ao desconectar ----

    private void StartInfo()
    {
        if (_infoTimer is null)
        {
            _info = new PcInfoReader();
            _infoTimer = _ui.CreateTimer();
            _infoTimer.Interval = InfoInterval;
            _infoTimer.IsRepeating = true;
            _infoTimer.Tick += OnInfoTick;
            _infoTimer.Start();
        }
        SendInfo();
    }

    private void StopInfo()
    {
        if (_infoTimer is null) return;
        _infoTimer.Stop();
        _infoTimer.Tick -= OnInfoTick;
        _infoTimer = null;
        _info?.Dispose();
        _info = null;
    }

    private void OnInfoTick(DispatcherQueueTimer sender, object args)
    {
        if (!_attached) return;
        SendInfo();
    }

    private void SendInfo()
    {
        if (_info is null) return;
        Post(RemoteProtocol.BuildInfo(_info.Read()));
    }

    private void ExecuteSleep(SleepCommand cmd)
    {
        if (cmd.Seconds > 0)
        {
            if (App.TestPc) App.Pc.ScheduleSleep(cmd.Seconds, testSeconds: true); // o seg só vale no --teste-pc (D24)
        }
        else if (cmd.Minutes > 0) App.Pc.ScheduleSleep(cmd.Minutes * 60, testSeconds: false);
        else App.Pc.CancelSleep();
    }

    /// <summary>Liga ou desliga a cópia da configuração do grupo entrada (D13). Thread da tela.</summary>
    private void SyncInput()
    {
        var s = App.Settings.Current;
        if (s.CelularLigado && RemoteTools.IsEnabled(s.FerramentasCelular, RemoteTools.Input))
        {
            lock (_inLock) _input ??= App.Pc.Actions;
        }
        else
        {
            DisableInput();
        }
    }

    /// <summary>Solta os botões que ficaram apertados e passa a ignorar a entrada.</summary>
    private void DisableInput()
    {
        lock (_inLock)
        {
            if (_input is { } a)
            {
                try { ReleaseHeld(a); }
                catch (Exception ex) { Log.Write($"ERRO celular: entrada: {ex.Message}"); }
            }
            _input = null;
            _held = 0;
        }
    }

    private void ReleaseHeld(IPcActions a)
    {
        foreach (var b in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
            if ((_held & (1 << (int)b)) != 0) a.MouseButtonSet(b, false);
        _held = 0;
    }

    private void OnPcChanged() => MarkDirty();

    private void OnBrightnessFailed(int tela) => Post(RemoteProtocol.BuildNotice("Não foi possível mudar o brilho desta tela"));

    private static bool HasTools() => App.Settings.Current.FerramentasCelular.Count > 0;

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
            var tools = App.Settings.Current.FerramentasCelular;
            var state = BuildState(_hub, App.Settings.Current.Favoritos) with
            {
                Tools = tools,
                TestMode = App.TestPc,
                Pc = BuildPc(tools, _shortcuts),
            };
            PostState(RemoteProtocol.BuildState(state));
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
            var info = hub.Media.FirstOrDefault(m => m.Key == g.Key);
            apps.Add(new RemoteApp(RemoteProtocol.IconId(g.Key), g.Key, g.Name, (int)Math.Round(Math.Clamp(g.Volume, 0f, 1f) * 100.0),
                g.Muted, g.IsSystem, routes, info is null ? null : ToRemote(info, g.Name)));
        }
        // linhas só de mídia (D9): no fim, sem volume e sem rotas
        foreach (var m in hub.Media)
        {
            if (m.Key is not null) continue;
            var key = MediaKeyPrefix + m.Aumid;
            var name = hub.MediaAppName(m.Aumid);
            apps.Add(new RemoteApp(RemoteProtocol.IconId(key), key, name, 0, false, false, Array.Empty<RemoteRoute>(), ToRemote(m, name), true));
        }

        return new RemoteState(outputs, hub.MasterAvailable, (int)Math.Round(Math.Clamp(hub.MasterVolume, 0f, 1f) * 100.0),
            hub.MasterMuted, apps);
    }

    /// <summary>O <c>pc</c> do estado (Fase 9, D6): só o que existe dos grupos ligados; nulo se nenhum tem o que mostrar. Thread da tela.</summary>
    private static RemotePcState? BuildPc(IReadOnlyList<string> tools, IReadOnlyList<string>? shortcuts)
    {
        var power = RemoteTools.IsEnabled(tools, RemoteTools.Power);
        var display = RemoteTools.IsEnabled(tools, RemoteTools.Display);
        var input = RemoteTools.IsEnabled(tools, RemoteTools.Input);
        var screen = RemoteTools.IsEnabled(tools, RemoteTools.Screen);
        var infoOn = RemoteTools.IsEnabled(tools, RemoteTools.Info);
        if (!power && !display && !input && !screen && !infoOn && shortcuts is null) return null;
        var pc = App.Pc;
        RemotePower? rp = null;
        if (power && pc.Plan is { } plan)
            rp = new RemotePower(PowerRules.Id(plan.Action), plan.DueAt.ToUnixTimeMilliseconds(),
                (int)Math.Ceiling((plan.DueAt - DateTimeOffset.UtcNow).TotalSeconds));
        RemoteProjection? pj = null;
        if (display)
        {
            var info = pc.Projection;
            pj = new RemoteProjection(info.Mode is { } m ? DisplayTopology.Id(m) : null, info.Screens);
        }
        List<RemoteScreen>? screens = null;
        if (screen)
        {
            screens = new List<RemoteScreen>();
            foreach (var m in ScreenCapture.Monitors()) screens.Add(new RemoteScreen(m.Index, m.Width, m.Height, m.Primary));
        }
        List<RemoteBrightness>? brightness = null;
        if (display)
        {
            brightness = new List<RemoteBrightness>();
            var values = pc.BrightnessValues;
            for (var i = 0; i < values.Count; i++) brightness.Add(new RemoteBrightness(i, values[i]));
        }
        RemoteSleep? sleep = null;
        if (infoOn && pc.Sleep is { } sp)
            sleep = new RemoteSleep(sp.DueAt.ToUnixTimeMilliseconds(), (int)Math.Ceiling((sp.DueAt - DateTimeOffset.UtcNow).TotalSeconds));
        return new RemotePcState(power, rp, pj, input, input && pc.Awake, screens, shortcuts, infoOn, sleep, brightness);
    }

    internal const string MediaKeyPrefix = "midia:";

    private static RemoteMedia ToRemote(MediaInfo m, string appName) =>
        new(string.IsNullOrWhiteSpace(m.Title) ? appName : m.Title, m.Artist, m.IsPlaying, m.CanPrev, m.CanPlayPause, m.CanNext);

    /// <summary>AUMID da mídia da linha (de som ou só de mídia), ou nulo quando a chave não existe.</summary>
    private string? MediaAumidOf(string key)
    {
        if (key.StartsWith(MediaKeyPrefix, StringComparison.Ordinal))
        {
            var aumid = key.Substring(MediaKeyPrefix.Length);
            foreach (var m in _hub.Media)
                if (m.Key is null && string.Equals(m.Aumid, aumid, StringComparison.Ordinal)) return m.Aumid;
            return null;
        }
        foreach (var m in _hub.Media)
            if (m.Key == key) return m.Aumid;
        return null;
    }

    // ---- Comandos (D8): validar e executar na thread da tela ----

    private void Execute(RemoteCommand command)
    {
        if (!_attached) return;
        // Fase 9, D6: comando de um grupo desligado é ignorado
        if (RemoteCommandGroups.GroupOf(command) is { } group && !RemoteTools.IsEnabled(App.Settings.Current.FerramentasCelular, group)) return;
        switch (command)
        {
            case RefreshCommand:
                if (RemoteTools.IsEnabled(App.Settings.Current.FerramentasCelular, RemoteTools.Display)) App.Pc.RefreshProjection();
                RefreshShortcuts();
                PushState();
                break;
            case WindowsCommand:
                SendWindows(force: true);
                break;
            case WindowActionCommand wa:
                App.Pc.ControlWindow(wa.Id, wa.Action);
                break;
            case ShortcutCommand sh:
                App.Pc.OpenShortcut(sh.Name);
                break;
            case SleepCommand sl:
                ExecuteSleep(sl);
                break;
            case ClipReadCommand:
                {
                    var (text, cut, failed) = App.Pc.ReadClipboard();
                    if (failed) { Post(RemoteProtocol.BuildNotice("Não consegui ler a área de transferência do PC")); break; }
                    Post(RemoteProtocol.BuildClip(text, cut));
                    if (cut) Post(RemoteProtocol.BuildNotice($"Texto cortado em {ClipboardBridge.MaxChars:N0} caracteres".Replace(',', '.')));
                    break;
                }
            case ClipWriteCommand cw:
                if (!App.Pc.WriteClipboard(cw.Text)) Post(RemoteProtocol.BuildNotice("Não consegui gravar na área de transferência do PC"));
                break;
            case SubscribeCommand sub:
                if (sub.Group == "janelas")
                {
                    if (sub.On) StartWindows();
                    else StopWindows();
                }
                else if (sub.Group == "info")
                {
                    if (sub.On) StartInfo();
                    else StopInfo();
                }
                break;
            case DisconnectCommand:
                Log.Write("celular: desconectado pelo celular");
                Finish(RemoteProtocol.BuildDisconnected(), 4000, "desconectado");
                break;
            case PowerCommand pw:
                App.Pc.SchedulePower(pw.Action, pw.Seconds);
                break;
            case PowerCancelCommand:
                App.Pc.CancelPower();
                break;
            case ProjectionCommand pj:
                if (!App.Pc.SetProjection(pj.Mode)) Post(RemoteProtocol.BuildNotice("O Windows recusou a troca de projeção"));
                break;
            case ScreensCommand sc:
                if (sc.Off) App.Pc.ScreensOff(); else App.Pc.ScreensOn();
                break;
            case BrightnessCommand br:
                App.Pc.RequestBrightness(br.Screen, br.Percent);
                break;
            case AwakeCommand aw:
                App.Pc.SetAwake(aw.On);
                break;
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
            case MediaCommand md when MediaAumidOf(md.App) is { } aumid:
                switch (md.Action)
                {
                    case MediaAction.Previous: _hub.MediaPrevious(aumid); break;
                    case MediaAction.PlayPause: _hub.MediaPlayPause(aumid); break;
                    case MediaAction.Next: _hub.MediaNext(aumid); break;
                }
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
                if (command is InputCommand input)
                {
                    RunInput(input); // D13: direto na thread de rede, sem passar pela fila da tela
                    continue;
                }
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

    /// <summary>
    /// Entrada do celular (D13 a D15), na thread de rede. Ignorada com o grupo desligado (<c>_input</c> nulo). Nada do que foi
    /// digitado vai para o log (D12).
    /// </summary>
    private void RunInput(InputCommand command)
    {
        lock (_inLock)
        {
            if (_input is not { } a) return;
            try
            {
                switch (command)
                {
                    case MouseMoveCommand m: a.MouseMove(m.Dx, m.Dy); break;
                    case ClickCommand c: a.MouseClick(c.Button, c.Double); break;
                    case ButtonCommand b:
                        a.MouseButtonSet(b.Button, b.Down);
                        if (b.Down) _held |= 1 << (int)b.Button; else _held &= ~(1 << (int)b.Button);
                        break;
                    case ScrollCommand s: a.MouseScroll(s.Dx, s.Dy); break;
                    case TextCommand t: a.TypeText(t.Text); break;
                    case KeyCommand k: a.PressKey(k.Key); break;
                    case MouseAbsCommand ab:
                        if (ScreenRules.AbsolutePoint(ScreenCapture.Monitors(), ab.Monitor, ab.X, ab.Y) is { } pt) a.MouseAbsolute(pt.X, pt.Y);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO celular: entrada: {ex.Message}");
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _signal.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                _signal.Reader.TryRead(out _);
                while (true)
                {
                    string? json;
                    (int Code, string Reason)? final = null;
                    lock (_outLock)
                    {
                        if (_queue.Count > 0) json = _queue.Dequeue();
                        else if (_latestState is not null) { json = _latestState; _latestState = null; }
                        else { json = null; final = _finalClose; }
                    }
                    if (json is null)
                    {
                        // Fila vazia: se havia um fechamento pedido, fecha com o código dele (4000, 4001...)
                        if (final is { } f)
                        {
                            await CloseSocketAsync((WebSocketCloseStatus)f.Code, f.Reason).ConfigureAwait(false);
                            return;
                        }
                        break;
                    }
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
