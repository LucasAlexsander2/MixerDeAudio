using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Services;
using NAudio.CoreAudioApi;

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
    private readonly Dictionary<string, MMDevice> _devices = new(StringComparer.Ordinal);
    private MMDeviceNotificationClient? _client;
    private MMDeviceEnumerator? _enumerator;
    private AudioEndpointVolume? _master;
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
    public event Action<MMDevice>? DeviceAdded;

    /// <summary>Uma saída saiu da lista; recebe o id. Disparado antes do Dispose do objeto da saída. Thread da tela.</summary>
    public event Action<string>? DeviceRemoved;

    /// <summary>O volume geral mudou de fora, ou a saída padrão mudou: disponível, volume de 0 a 1, mudo. Thread da tela.</summary>
    public event Action<bool, float, bool>? MasterChanged;

    /// <summary>Entrega à tela o estado atual do volume geral (o <see cref="Start"/> roda antes da ligação do evento).</summary>
    public void PublishCurrentMaster() => PublishMaster();

    /// <summary>Saídas ativas, para o monitor de sessões ler ao abrir.</summary>
    public IReadOnlyCollection<MMDevice> Devices => _devices.Values;

    /// <summary>Lê as saídas ativas e a padrão e se inscreve nos eventos. Na thread da tela.</summary>
    public void Start()
    {
        _enumerator = new MMDeviceEnumerator();
        Resync(raiseAdded: false);
        try
        {
            if (_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var def))
            {
                using (def) Tracker.SetDefault(def.ID);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO COM ao ler a saída padrão: {ex.Message}");
        }
        RefreshMaster();
        _client = _enumerator.CreateNotificationClient(false);
        _client.DefaultDeviceChanged += OnDefaultDeviceChanged;
        _client.DeviceAdded += OnDeviceAdded;
        _client.DeviceRemoved += OnDeviceRemoved;
        _client.DeviceStateChanged += OnDeviceStateChanged;
    }

    // ---- Eventos do Windows (threads de fundo): só copiam e enfileiram (D2) ----

    // Desvio da análise: o IMMNotificationClient é interno no NAudio 3.3.0; usa-se o MMDeviceNotificationClient
    // (CreateNotificationClient(false): eventos na thread de áudio do Windows, sem SynchronizationContext).
    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        // D3: só Render e o papel Multimedia, para não tratar o mesmo evento três vezes.
        if (e.Flow != DataFlow.Render || e.Role != Role.Multimedia) return;
        var id = e.DeviceId;
        Post(() => HandleDefault(id));
    }

    private void OnDeviceAdded(object? sender, DeviceNotificationEventArgs e) => PostResync();
    private void OnDeviceRemoved(object? sender, DeviceNotificationEventArgs e) => PostResync();
    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e) => PostResync();
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
        var fresh = new List<MMDevice>();
        using (var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            for (var i = 0; i < collection.Count; i++)
            {
                try
                {
                    var device = collection[i];
                    var id = device.ID;
                    if (_devices.ContainsKey(id) || !seen.Add(id)) { seen.Add(id); SafeDispose(device); continue; }
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
            _devices[device.ID] = device;
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
                var props = device.Properties;
                desc = ReadString(props, PropertyKeys.PKEY_Device_DeviceDesc);
                full = ReadString(props, PropertyKeys.PKEY_Device_FriendlyName);
                if (props.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor)
                    && props[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value is { } v)
                    form = Convert.ToInt32(v);
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO COM ao ler propriedades da saída: {ex.Message}");
            }
            list.Add(DeviceInfo.Create(id, desc, full, form));
        }
        return list;
    }

    private static string? ReadString(PropertyStore props, PropertyKey key) =>
        props.Contains(key) ? props[key].Value as string : null;

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
                var volume = device.AudioEndpointVolume;
                volume.OnVolumeNotification += OnMasterNotification;
                _master = volume;
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
            try { _master.OnVolumeNotification -= OnMasterNotification; }
            catch (Exception ex) { Log.Write($"ERRO COM ao cancelar o volume geral: {ex.Message}"); }
        }
        _master = null;
        _masterId = null;
        _masterDirty = true;
    }

    private void PublishMaster()
    {
        _masterDirty = false;
        if (_master is null) { MasterChanged?.Invoke(false, 0f, false); return; }
        try { MasterChanged?.Invoke(true, _master.MasterVolumeLevelScalar, _master.Mute); }
        catch (Exception ex) { Log.Write($"ERRO COM ao ler o volume geral: {ex.Message}"); }
    }

    // Thread de fundo: só copia e enfileira (D2).
    private void OnMasterNotification(AudioVolumeNotificationData data)
    {
        var volume = data.MasterVolume;
        var muted = data.Muted;
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
        try { _master.MasterVolumeLevelScalar = volume; }
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
                _client.DefaultDeviceChanged -= OnDefaultDeviceChanged;
                _client.DeviceAdded -= OnDeviceAdded;
                _client.DeviceRemoved -= OnDeviceRemoved;
                _client.DeviceStateChanged -= OnDeviceStateChanged;
                _client.Dispose();
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
