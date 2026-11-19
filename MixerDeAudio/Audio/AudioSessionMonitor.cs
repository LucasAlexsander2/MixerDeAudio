using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Services;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MixerDeAudio.Audio;

/// <summary>
/// Liga o NAudio ao <see cref="SessionTracker"/> (D16). Existe só com a janela aberta (D1): ao abrir lista as
/// sessões de todas as saídas ativas (D2) e se inscreve nos eventos; o <see cref="Dispose"/> solta tudo.
/// Desde a Fase 3 não tem enumerador próprio: as saídas vêm do <see cref="DeviceMonitor"/> (D1), por
/// <see cref="AddDevice"/> e <see cref="RemoveDevice"/> (D4).
/// Toda chamada COM acontece na thread da tela (D4): os tratadores de evento só copiam os valores e enfileiram.
/// </summary>
internal sealed class AudioSessionMonitor : IDisposable
{
    private sealed class Entry
    {
        public required string Id;
        public required string DeviceId;
        public required AudioSessionControl Control;
        public required Handler Handler;
        public float Volume;
        public bool Muted;
    }

    private readonly DispatcherQueue _queue;
    private readonly SessionTracker _tracker;
    private readonly VolumeEchoFilter _echo = new();
    private readonly ProcessInfo _processes = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (AudioSessionManager Manager, AudioSessionManager.SessionCreatedDelegate Handler)> _managers = new(StringComparer.Ordinal);
    private volatile bool _disposed;

    public AudioSessionMonitor(DispatcherQueue queue, SessionTracker tracker)
    {
        _queue = queue;
        _tracker = tracker;
    }

    /// <summary>Lê as sessões das saídas dadas e se inscreve nos eventos. Na thread da tela.</summary>
    public void Start(IEnumerable<MMDevice> devices)
    {
        foreach (var device in devices) AddDevice(device);
    }

    /// <summary>Uma saída entrou (ou existia ao abrir): lê as sessões dela e se inscreve em "sessão nova" (D4).</summary>
    public void AddDevice(MMDevice device)
    {
        if (_disposed) return;
        try
        {
            var deviceId = device.ID;
            if (_managers.ContainsKey(deviceId)) return;
            var manager = device.AudioSessionManager;

            // D7: a lista de sessões precisa ser lida antes de se inscrever em "sessão nova".
            var sessions = manager.Sessions;
            for (var s = 0; s < sessions.Count; s++)
            {
                try { AddSession(deviceId, sessions[s]); }
                catch (Exception ex) { Log.Write($"ERRO COM ao ler sessão: {ex.Message}"); }
            }

            AudioSessionManager.SessionCreatedDelegate onCreated = (_, _) => OnSessionCreated(deviceId);
            manager.OnSessionCreated += onCreated;
            _managers[deviceId] = (manager, onCreated);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO COM ao abrir saída de áudio: {ex.Message}");
        }
    }

    /// <summary>
    /// Uma saída saiu: tira as sessões dela da lista e solta o gerenciador, pelo mesmo caminho do
    /// <see cref="RemoveEntry"/> (D4). As sessões também podem ter chegado desconectadas antes; os dois sentidos funcionam.
    /// </summary>
    public void RemoveDevice(string deviceId)
    {
        if (_disposed) return;
        if (_managers.Remove(deviceId, out var m))
        {
            try { m.Manager.OnSessionCreated -= m.Handler; }
            catch (Exception ex) { Log.Write($"ERRO COM ao cancelar sessão nova: {ex.Message}"); }
        }
        var ids = new List<string>();
        foreach (var entry in _entries.Values)
            if (entry.DeviceId == deviceId) ids.Add(entry.Id);
        foreach (var id in ids) RemoveEntry(id);
        if (m.Manager is not null) SafeDispose(m.Manager);
    }

    // ---- Eventos do Windows (threads de fundo): só copiam e enfileiram (D4) ----

    // Desvio do D4: o NAudio 3.x entrega a sessão nova como IAudioSessionControl, que é interna (não dá para criar o
    // AudioSessionControl aqui). O tratador só enfileira e a thread da tela relê a lista do gerenciador.
    private void OnSessionCreated(string deviceId) => Post(() => Rescan(deviceId));

    /// <summary>Relê as sessões do gerenciador da saída e adiciona as que ainda não estão na lista.</summary>
    private void Rescan(string deviceId)
    {
        // A saída pode ter saído entre o evento e esta chamada (D4): sem gerenciador, nada a fazer.
        if (!_managers.TryGetValue(deviceId, out var m)) return;
        var manager = m.Manager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (var s = 0; s < sessions.Count; s++)
        {
            try { AddSession(deviceId, sessions[s]); }
            catch (Exception ex) { Log.Write($"ERRO COM ao ler sessão nova: {ex.Message}"); }
        }
    }

    /// <summary>Enfileira uma ação para a thread da tela; ignorada se o monitor já recebeu Dispose (D6).</summary>
    private void Post(Action action)
    {
        if (_disposed) return;
        _queue.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { action(); }
            catch (Exception ex) { Log.Write($"ERRO COM em evento de sessão: {ex.Message}"); }
        });
    }

    private sealed class Handler : IAudioSessionEventsHandler
    {
        private readonly AudioSessionMonitor _owner;
        private readonly string _id;

        public Handler(AudioSessionMonitor owner, string id)
        {
            _owner = owner;
            _id = id;
        }

        public void OnVolumeChanged(float volume, bool isMuted) => _owner.Post(() => _owner.HandleVolume(_id, volume, isMuted));
        public void OnDisplayNameChanged(string displayName) => _owner.Post(() => _owner.HandleName(_id));
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) => _owner.Post(() => _owner.HandleState(_id, state));
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => _owner.Post(() => _owner.RemoveEntry(_id));
    }

    // ---- Na thread da tela ----

    private void AddSession(string deviceId, AudioSessionControl control)
    {
        var added = false;
        try
        {
            var id = control.GetSessionInstanceIdentifier;
            if (string.IsNullOrEmpty(id) || _entries.ContainsKey(id)) return;
            var state = Map(control.State);
            if (state == SessionState.Expired) return;

            var isSystem = control.IsSystemSoundsSession;
            var pid = (int)control.GetProcessID;
            var proc = isSystem ? default : _processes.Get(pid);
            var name = AppNameResolver.Resolve(isSystem, ProcessInfo.ResolveIndirect(control.DisplayName), proc.FileDescription, proc.ProcessName);
            var volume = control.SimpleAudioVolume.Volume;
            var muted = control.SimpleAudioVolume.Mute;

            var handler = new Handler(this, id);
            control.RegisterEventClient(handler);
            _entries[id] = new Entry { Id = id, DeviceId = deviceId, Control = control, Handler = handler, Volume = volume, Muted = muted };
            added = true;
            _tracker.Add(new SessionInfo(id, pid, proc.ExePath, name, volume, muted, state, isSystem));
        }
        finally
        {
            if (!added) SafeDispose(control);
        }
    }

    private void HandleVolume(string id, float volume, bool muted)
    {
        if (!_entries.TryGetValue(id, out var entry)) return;
        var key = _tracker.KeyOf(id);
        if (key is null) return;
        if (_echo.IsEcho(key, volume)) return; // D14: eco do próprio ajuste
        entry.Volume = volume;
        entry.Muted = muted;
        _tracker.UpdateVolume(id, volume, muted);
    }

    private void HandleState(string id, AudioSessionState state)
    {
        var mapped = Map(state);
        if (mapped == SessionState.Expired) RemoveEntry(id);
        else _tracker.UpdateState(id, mapped);
    }

    private void HandleName(string id)
    {
        if (!_entries.TryGetValue(id, out var entry)) return;
        var key = _tracker.KeyOf(id);
        if (key is null) return;
        var control = entry.Control;
        var isSystem = control.IsSystemSoundsSession;
        var proc = isSystem ? default : _processes.Get((int)control.GetProcessID);
        _tracker.UpdateName(id, AppNameResolver.Resolve(isSystem, ProcessInfo.ResolveIndirect(control.DisplayName), proc.FileDescription, proc.ProcessName));
    }

    /// <summary>Tira a sessão da lista e solta os objetos COM dela (D5: sempre fora do tratador do evento).</summary>
    private void RemoveEntry(string id)
    {
        if (!_entries.Remove(id, out var entry)) return;
        _tracker.Remove(id);
        try { entry.Control.UnRegisterEventClient(entry.Handler); }
        catch (Exception ex) { Log.Write($"ERRO COM ao cancelar inscrição: {ex.Message}"); }
        SafeDispose(entry.Control);
    }

    // ---- Comandos da tela ----

    /// <summary>Aplica o volume (0 a 1) a todas as sessões do grupo. Erro de COM é registrado e ignorado (D16).</summary>
    public void SetVolume(string key, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        _echo.Register(key, volume);
        foreach (var id in _tracker.SessionIdsOf(key))
        {
            if (!_entries.TryGetValue(id, out var entry)) continue;
            try
            {
                entry.Control.SimpleAudioVolume.Volume = volume;
                entry.Volume = volume;
                _tracker.UpdateVolume(id, volume, entry.Muted);
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO COM ao gravar volume: {ex.Message}");
            }
        }
    }

    /// <summary>Aplica o mudo a todas as sessões do grupo.</summary>
    public void SetMute(string key, bool muted)
    {
        foreach (var id in _tracker.SessionIdsOf(key))
        {
            if (!_entries.TryGetValue(id, out var entry)) continue;
            try
            {
                entry.Control.SimpleAudioVolume.Mute = muted;
                entry.Muted = muted;
                _tracker.UpdateVolume(id, entry.Volume, muted);
            }
            catch (Exception ex)
            {
                Log.Write($"ERRO COM ao gravar mudo: {ex.Message}");
            }
        }
    }

    private static SessionState Map(AudioSessionState state) => state switch
    {
        AudioSessionState.AudioSessionStateActive => SessionState.Active,
        AudioSessionState.AudioSessionStateExpired => SessionState.Expired,
        _ => SessionState.Inactive,
    };

    private static void SafeDispose(IDisposable? d)
    {
        try { d?.Dispose(); }
        catch (Exception ex) { Log.Write($"ERRO COM no Dispose: {ex.Message}"); }
    }

    /// <summary>Solta tudo, na ordem do D16: inscrições, sessões e gerenciadores (saídas e enumerador são do DeviceMonitor).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (manager, handler) in _managers.Values)
        {
            try { manager.OnSessionCreated -= handler; }
            catch (Exception ex) { Log.Write($"ERRO COM ao cancelar sessão nova: {ex.Message}"); }
        }
        foreach (var entry in _entries.Values)
        {
            try { entry.Control.UnRegisterEventClient(entry.Handler); }
            catch (Exception ex) { Log.Write($"ERRO COM ao cancelar inscrição: {ex.Message}"); }
        }
        foreach (var entry in _entries.Values) SafeDispose(entry.Control);
        _entries.Clear();
        foreach (var (manager, _) in _managers.Values) SafeDispose(manager);
        _managers.Clear();
        // As saídas e o enumerador são do DeviceMonitor, que recebe o Dispose depois deste (D1).
        _processes.Clear();
    }
}
