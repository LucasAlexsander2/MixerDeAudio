using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Services;
using MixerDeAudio.Audio.CoreAudio;

namespace MixerDeAudio.Audio;

/// <summary>
/// Dono do enumerador de saídas, do <c>IMMNotificationClient</c> e da lista de saídas ativas (D1). Existe só com a
/// janela aberta. Também cuida do volume geral, que é o da saída padrão (D11). Mesmas regras de thread da Fase 2 (D2):
/// os tratadores dos eventos só copiam valores e enfileiram na thread da tela; ali nunca se chama COM, cancela
/// inscrição ou dá Dispose. Depois do <see cref="Dispose"/>, o que ainda chegar é ignorado.
/// </summary>
internal sealed class DeviceMonitor : IDisposable
{
    public const string MasterEchoKey = "*geral*";

    private readonly DispatcherQueue _queue;
    private readonly VolumeEchoFilter _echo = new();
    private readonly Dictionary<string, CoreDevice> _devices = new(StringComparer.Ordinal);
    private DeviceNotificationSink? _client;
    private CoreEnumerator? _enumerator;
    private CoreEndpointVolume? _master;
    private string? _masterId;
    private bool _masterDirty = true; // há mudança no volume geral que a tela ainda não recebeu
    private bool _resyncPending;
    private volatile bool _disposed;

    public DeviceMonitor(DispatcherQueue queue)
    {
        _queue = queue;
    }

    public DeviceTracker Tracker { get; } = new();

    /// <summary>Uma saída entrou depois do <see cref="Start"/>. Thread da tela.</summary>
    public event Action<CoreDevice>? DeviceAdded;

    /// <summary>Uma saída saiu da lista; recebe o id. Disparado antes do Dispose do objeto da saída. Thread da tela.</summary>
    public event Action<string>? DeviceRemoved;

    /// <summary>O volume geral mudou de fora, ou a saída padrão mudou: disponível, volume de 0 a 1, mudo. Thread da tela.</summary>
    public event Action<bool, float, bool>? MasterChanged;

    /// <summary>Entrega à tela o estado atual do volume geral (o <see cref="Start"/> roda antes da ligação do evento).</summary>
    public void PublishCurrentMaster() => PublishMaster();

    /// <summary>Saídas ativas, para o monitor de sessões ler ao abrir.</summary>
    public IReadOnlyCollection<CoreDevice> Devices => _devices.Values;

    /// <summary>Lê as saídas ativas e a padrão e se inscreve nos eventos. Na thread da tela.</summary>
    public void Start()
    {
        _enumerator = new CoreEnumerator();
        Resync(raiseAdded: false);
        try
        {
            var defaultId = _enumerator.GetDefaultRenderId();
            if (defaultId is not null) Tracker.SetDefault(defaultId);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO COM ao ler a saída padrão: {ex.Message}");
        }
        RefreshMaster();
        _client = new DeviceNotificationSink();
        _client.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _client.DeviceAdded += PostResync;
        _client.DeviceRemoved += PostResync;
        _client.DeviceStateChanged += PostResync;
        _enumerator.Register(_client);
    }

    // ---- Eventos do Windows (threads de fundo): só copiam e enfileiram (D2) ----

    // Os avisos chegam na thread de áudio do Windows (Fase 8, D8: interface própria no lugar do NAudio).
    private void OnDefaultDeviceChanged(int flow, int role, string? deviceId)
    {
        // D3: só Render e o papel Multimedia, para não tratar o mesmo evento três vezes.
        if (flow != CoreAudioCom.Render || role != CoreAudioCom.RoleMultimedia) return;
        Post(() => HandleDefault(deviceId));
    }

    // D3: OnPropertyValueChanged dispara muitas vezes e é ignorado (sem inscrição).

    /// <summary>Várias notificações seguidas (ligar um fone gera várias) viram uma releitura só.</summary>
    private void PostResync()
    {
        if (_disposed) return;
        _queue.TryEnqueue(() =>
        {
            if (_disposed || _resyncPending) return;
            _resyncPending = true;
            try
            {
                Resync(raiseAdded: true);
                RefreshMaster();
            }
            catch (Exception ex) { Log.Write($"ERRO COM ao reler as saídas: {ex.Message}"); }
            finally { _resyncPending = false; }
        });
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        _queue.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { action(); }
            catch (Exception ex) { Log.Write($"ERRO COM em evento de saída: {ex.Message}"); }
        });
    }

    // ---- Na thread da tela ----

    private void HandleDefault(string? id)
    {
        Tracker.SetDefault(string.IsNullOrEmpty(id) ? null : id);
        RefreshMaster();
    }

    /// <summary>Relê a lista de saídas ativas e compara com a atual pelo id (D3 e D4).</summary>
    private void Resync(bool raiseAdded)
    {
        if (_enumerator is null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fresh = new List<CoreDevice>();
        using (var collection = _enumerator.EnumerateActiveRender())
        {
            for (var i = 0; i < collection.Count; i++)
            {
                try
                {
                    var device = collection[i];
                    var id = device.Id;
                    if (_devices.ContainsKey(id) || !seen.Add(id)) { seen.Add(id); ReleaseDuplicate(device); continue; }
                    fresh.Add(device);
                }
                catch (Exception ex)
                {
                    Log.Write($"ERRO COM ao abrir saída de áudio: {ex.Message}");
                }
            }
        }

        // Saídas que sumiram: avisar o monitor de sessões antes de soltar o objeto.
        foreach (var id in new List<string>(_devices.Keys))
        {
            if (seen.Contains(id)) continue;
            if (id == _masterId) ReleaseMaster();
            _devices.Remove(id, out var gone);
            DeviceRemoved?.Invoke(id);
            SafeDispose(gone);
        }

        // Saídas novas.
        foreach (var device in fresh)
        {
            _devices[device.Id] = device;
            if (raiseAdded) DeviceAdded?.Invoke(device);
        }

        Tracker.Sync(BuildInfos());
    }

    private List<DeviceInfo> BuildInfos()
    {
        var list = new List<DeviceInfo>(_devices.Count);
        foreach (var (id, device) in _devices)
        {
            string? desc = null, full = null;
            var form = 0;
            try
            {
                (desc, full, form) = device.ReadProperties();
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO COM ao ler propriedades da saída: {ex.Message}");
            }
            list.Add(DeviceInfo.Create(id, desc, full, form));
        }
        return list;
    }

    /// <summary>
    /// Solta uma leitura repetida da mesma saída. Se o Windows devolveu o mesmo objeto que já temos (o COM reconhece
    /// pela identidade), soltar mataria o objeto em uso: nesse caso não se solta nada.
    /// </summary>
    private void ReleaseDuplicate(CoreDevice device)
    {
        if (_devices.TryGetValue(device.Id, out var known) && ReferenceEquals(known, device)) return;
        SafeDispose(device);
    }

    // ---- Volume geral (D11 e D12) ----

    /// <summary>Acompanha o volume da saída ativa: cancela a inscrição antiga e faz a nova (D11).</summary>
    private void RefreshMaster()
    {
        var activeId = Tracker.ActiveId;
        if (!_masterDirty && activeId == _masterId && (_master is not null || activeId is null)) return;
        ReleaseMaster();
        if (activeId is not null && _devices.TryGetValue(activeId, out var device))
        {
            try
            {
                var volume = device.OpenEndpointVolume();
                _master = volume;
                volume.Subscribe(OnMasterNotification);
                _masterId = activeId;
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO COM ao abrir o volume geral: {ex.Message}");
                _master = null;
                _masterId = null;
            }
        }
        PublishMaster();
    }

    private void ReleaseMaster()
    {
        if (_master is not null)
        {
            try { _master.Unsubscribe(); }
            catch (Exception ex) { Log.Write($"ERRO COM ao cancelar o volume geral: {ex.Message}"); }
            SafeDispose(_master);
        }
        _master = null;
        _masterId = null;
        _masterDirty = true;
    }

    private void PublishMaster()
    {
        _masterDirty = false;
        if (_master is null) { MasterChanged?.Invoke(false, 0f, false); return; }
        try { MasterChanged?.Invoke(true, _master.VolumeScalar, _master.Mute); }
        catch (Exception ex) { Log.Write($"ERRO COM ao ler o volume geral: {ex.Message}"); }
    }

    // Thread de fundo: só copia e enfileira (D2).
    private void OnMasterNotification(float volume, bool muted)
    {
        var id = _masterId;
        Post(() =>
        {
            if (id != _masterId || _master is null) return; // notificação de uma saída que já deixou de ser a padrão
            if (_echo.IsEcho(MasterEchoKey, volume)) return; // D12: eco do próprio ajuste
            MasterChanged?.Invoke(true, volume, muted);
        });
    }

    /// <summary>Grava o volume geral (0 a 1) na saída padrão. Erro de COM é registrado e ignorado.</summary>
    public void SetMasterVolume(float volume)
    {
        if (_master is null) return;
        volume = Math.Clamp(volume, 0f, 1f);
        _echo.Register(MasterEchoKey, volume);
        try { _master.VolumeScalar = volume; }
        catch (Exception ex) { Log.Write($"ERRO COM ao gravar o volume geral: {ex.Message}"); }
    }

    /// <summary>Grava o mudo geral na saída padrão.</summary>
    public void SetMasterMute(bool muted)
    {
        if (_master is null) return;
        try { _master.Mute = muted; }
        catch (Exception ex) { Log.Write($"ERRO COM ao gravar o mudo geral: {ex.Message}"); }
    }

    // ---- Troca da saída padrão (D10) ----

    /// <summary>
    /// Torna a saída a padrão do Windows. Não muda o destaque: ele só muda pelo evento (única fonte da verdade).
    /// Clicar na que já é a padrão não faz nada.
    /// </summary>
    public void SetDefaultDevice(string id)
    {
        if (Tracker.IsActive(id)) return;
        var name = "?";
        foreach (var d in Tracker.Devices)
            if (d.Id == id) name = d.ShortName;
        try
        {
            PolicyConfig.SetDefault(id);
            Log.Write($"saida padrao trocada para {name}");
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao trocar a saida padrao para {name}: {ex.Message}");
        }
    }

    private static void SafeDispose(IDisposable? d)
    {
        try { d?.Dispose(); }
        catch (Exception ex) { Log.Write($"ERRO COM no Dispose: {ex.Message}"); }
    }

    /// <summary>Solta tudo: inscrições, volume geral, saídas e enumerador.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_client is not null)
            {
                _enumerator?.Unregister();
                _client.Detach();
            }
        }
        catch (Exception ex) { Log.Write($"ERRO COM ao cancelar notificação de saídas: {ex.Message}"); }
        _client = null;
        ReleaseMaster();
        foreach (var device in _devices.Values) SafeDispose(device);
        _devices.Clear();
        SafeDispose(_enumerator);
        _enumerator = null;
    }
}
