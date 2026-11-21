using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MixerDeAudio.Audio.CoreAudio;

// Camadas finas sobre as interfaces do Core Audio (Fase 8, D8): guardam o objeto COM e o soltam de forma determinística
// (FinalRelease) no Dispose, que é o que o NAudio fazia. Os métodos de leitura e gravação lançam exceção se o Windows
// recusar, como o NAudio; quem chama trata e registra.

internal static partial class CoreAudioCom
{
    private const int ClsctxAll = 23; // INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER
    private static readonly Guid EnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorInterfaceId = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    internal static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    internal static readonly Guid SessionManager2Id = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    /// <summary>Contexto de evento vazio: a chamada não carrega identificação de quem mudou o volume.</summary>
    internal static readonly Guid NoContext = Guid.Empty;

    internal const int Render = 0;
    internal const int RoleMultimedia = 1;
    internal const int StateActive = 1;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid, out IntPtr instance);

    [LibraryImport("ole32.dll")]
    internal static partial int PropVariantClear(ref PropVariant value);

    /// <summary>Cria o enumerador de dispositivos (<c>MMDeviceEnumerator</c>).</summary>
    internal static IMMDeviceEnumerator CreateEnumerator()
    {
        Marshal.ThrowExceptionForHR(CoCreateInstance(EnumeratorClassId, IntPtr.Zero, ClsctxAll, EnumeratorInterfaceId, out var ptr));
        try
        {
            return (IMMDeviceEnumerator)AumidResolver.Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>Pede uma interface ao dispositivo (<c>IMMDevice.Activate</c>).</summary>
    internal static T Activate<T>(IMMDevice device, Guid iid) where T : class
    {
        device.Activate(iid, ClsctxAll, IntPtr.Zero, out var ptr);
        try
        {
            return (T)AumidResolver.Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>Solta o objeto COM agora (sem esperar o coletor de lixo). Nulo e objeto que não é COM são ignorados.</summary>
    internal static void Release(object? comObject)
    {
        if (comObject is ComObject c) c.FinalRelease();
    }
}

/// <summary>Enumerador de dispositivos; dono do registro das notificações de saída.</summary>
internal sealed class CoreEnumerator : IDisposable
{
    private IMMDeviceEnumerator? _enumerator;
    private DeviceNotificationSink? _registered;

    public CoreEnumerator()
    {
        _enumerator = CoreAudioCom.CreateEnumerator();
    }

    /// <summary>Id da saída padrão (papel Multimedia), ou nulo se o Windows não tem nenhuma.</summary>
    public string? GetDefaultRenderId()
    {
        if (_enumerator is null) return null;
        var hr = _enumerator.GetDefaultAudioEndpoint(CoreAudioCom.Render, CoreAudioCom.RoleMultimedia, out var device);
        if (hr != 0) return null;
        try
        {
            device.GetId(out var id);
            return id;
        }
        finally
        {
            CoreAudioCom.Release(device);
        }
    }

    /// <summary>Saídas de áudio ativas. A coleção devolvida tem que receber Dispose.</summary>
    public CoreDeviceCollection EnumerateActiveRender()
    {
        _enumerator!.EnumAudioEndpoints(CoreAudioCom.Render, CoreAudioCom.StateActive, out var collection);
        return new CoreDeviceCollection(collection);
    }

    public void Register(DeviceNotificationSink sink)
    {
        _enumerator!.RegisterEndpointNotificationCallback(sink);
        _registered = sink;
    }

    public void Unregister()
    {
        var sink = _registered;
        _registered = null;
        if (sink is not null) _enumerator?.UnregisterEndpointNotificationCallback(sink);
    }

    public void Dispose()
    {
        CoreAudioCom.Release(_enumerator);
        _enumerator = null;
    }
}

internal sealed class CoreDeviceCollection : IDisposable
{
    private IMMDeviceCollection? _collection;

    internal CoreDeviceCollection(IMMDeviceCollection collection)
    {
        _collection = collection;
        collection.GetCount(out var count);
        Count = (int)count;
    }

    public int Count { get; }

    public CoreDevice this[int index]
    {
        get
        {
            _collection!.Item((uint)index, out var device);
            return new CoreDevice(device);
        }
    }

    public void Dispose()
    {
        CoreAudioCom.Release(_collection);
        _collection = null;
    }
}

/// <summary>Uma saída de áudio (<c>IMMDevice</c>).</summary>
internal sealed class CoreDevice : IDisposable
{
    private IMMDevice? _device;

    internal CoreDevice(IMMDevice device)
    {
        _device = device;
        try
        {
            device.GetId(out var id);
            Id = id;
        }
        catch
        {
            CoreAudioCom.Release(device);
            _device = null;
            throw;
        }
    }

    public string Id { get; }

    /// <summary>Descrição, nome amigável e forma do aparelho (<c>EndpointFormFactor</c>); nulo / 0 se a propriedade não existe.</summary>
    public (string? Description, string? FriendlyName, int FormFactor) ReadProperties()
    {
        _device!.OpenPropertyStore(0 /* STGM_READ */, out var store);
        try
        {
            return (ReadString(store, PropertyKey.DeviceDesc), ReadString(store, PropertyKey.FriendlyName), ReadInt(store, PropertyKey.FormFactor));
        }
        finally
        {
            CoreAudioCom.Release(store);
        }
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(key, out var value);
        try { return value.AsString(); }
        finally { CoreAudioCom.PropVariantClear(ref value); }
    }

    private static int ReadInt(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(key, out var value);
        try { return value.AsInt32() ?? 0; }
        finally { CoreAudioCom.PropVariantClear(ref value); }
    }

    /// <summary>Abre o volume geral da saída.</summary>
    public CoreEndpointVolume OpenEndpointVolume() =>
        new(CoreAudioCom.Activate<IAudioEndpointVolume>(_device!, CoreAudioCom.EndpointVolumeId));

    /// <summary>Abre o gerenciador de sessões da saída.</summary>
    public CoreSessionManager OpenSessionManager() =>
        new(CoreAudioCom.Activate<IAudioSessionManager2>(_device!, CoreAudioCom.SessionManager2Id));

    public void Dispose()
    {
        CoreAudioCom.Release(_device);
        _device = null;
    }
}

/// <summary>Volume geral de uma saída (<c>IAudioEndpointVolume</c>), com o aviso de mudança.</summary>
internal sealed class CoreEndpointVolume : IDisposable
{
    private IAudioEndpointVolume? _volume;
    private EndpointVolumeSink? _sink;

    internal CoreEndpointVolume(IAudioEndpointVolume volume)
    {
        _volume = volume;
    }

    public float VolumeScalar
    {
        get { _volume!.GetMasterVolumeLevelScalar(out var v); return v; }
        set => _volume!.SetMasterVolumeLevelScalar(value, CoreAudioCom.NoContext);
    }

    public bool Mute
    {
        get { _volume!.GetMute(out var m); return m; }
        set => _volume!.SetMute(value, CoreAudioCom.NoContext);
    }

    /// <summary>Passa a avisar mudanças (volume de 0 a 1, mudo) numa thread de fundo do Windows.</summary>
    public void Subscribe(Action<float, bool> onChanged)
    {
        if (_sink is not null) return;
        var sink = new EndpointVolumeSink(onChanged);
        _volume!.RegisterControlChangeNotify(sink);
        _sink = sink;
    }

    /// <summary>Cancela o aviso (se havia).</summary>
    public void Unsubscribe()
    {
        var sink = _sink;
        _sink = null;
        if (sink is not null) _volume?.UnregisterControlChangeNotify(sink);
    }

    public void Dispose()
    {
        CoreAudioCom.Release(_volume);
        _volume = null;
    }
}

/// <summary>Gerenciador de sessões de uma saída (<c>IAudioSessionManager2</c>).</summary>
internal sealed class CoreSessionManager : IDisposable
{
    private IAudioSessionManager2? _manager;
    private IAudioSessionEnumerator? _enumerator;
    private SessionNotificationSink? _sink;

    internal CoreSessionManager(IAudioSessionManager2 manager)
    {
        _manager = manager;
    }

    /// <summary>
    /// Lê as sessões atuais. O enumerador é pedido a cada leitura e o anterior é solto depois; o primeiro pedido tem que
    /// vir antes do <see cref="Subscribe"/> (o Windows só avisa "sessão nova" depois de a lista ter sido pedida).
    /// Cada <see cref="CoreSession"/> devolvida tem que receber Dispose, ou ser entregue a quem o dará.
    /// </summary>
    public List<CoreSession> GetSessions()
    {
        _manager!.GetSessionEnumerator(out var fresh);
        var old = _enumerator;
        _enumerator = fresh;
        CoreAudioCom.Release(old);

        fresh.GetCount(out var count);
        var list = new List<CoreSession>(count);
        for (var i = 0; i < count; i++)
        {
            fresh.GetSession(i, out var control);
            list.Add(new CoreSession(control));
        }
        return list;
    }

    /// <summary>Passa a avisar "sessão nova" numa thread de fundo do Windows.</summary>
    public void Subscribe(Action onSessionCreated)
    {
        if (_sink is not null) return;
        var sink = new SessionNotificationSink(onSessionCreated);
        _manager!.RegisterSessionNotification(sink);
        _sink = sink;
    }

    public void Unsubscribe()
    {
        var sink = _sink;
        _sink = null;
        if (sink is not null) _manager?.UnregisterSessionNotification(sink);
    }

    public void Dispose()
    {
        CoreAudioCom.Release(_enumerator);
        _enumerator = null;
        CoreAudioCom.Release(_manager);
        _manager = null;
    }
}

/// <summary>Uma sessão de áudio (<c>IAudioSessionControl2</c> e o <c>ISimpleAudioVolume</c> do mesmo objeto).</summary>
internal sealed class CoreSession : IDisposable
{
    private IAudioSessionControl2? _control;
    private IAudioSessionEvents? _events;

    internal CoreSession(IAudioSessionControl2 control)
    {
        _control = control;
    }

    /// <summary>O objeto COM por baixo: duas leituras que dão o mesmo objeto são a mesma sessão já conhecida.</summary>
    internal object? Native => _control;

    public string InstanceId { get { _control!.GetSessionInstanceIdentifier(out var id); return id ?? ""; } }
    public string DisplayName { get { _control!.GetDisplayName(out var name); return name ?? ""; } }
    public int State { get { _control!.GetState(out var s); return s; } }
    public int ProcessId { get { _control!.GetProcessId(out var p); return (int)p; } }
    public bool IsSystemSounds => _control!.IsSystemSoundsSession() == 0;

    private ISimpleAudioVolume Simple => (ISimpleAudioVolume)_control!;

    public float Volume
    {
        get { Simple.GetMasterVolume(out var v); return v; }
        set => Simple.SetMasterVolume(value, CoreAudioCom.NoContext);
    }

    public bool Mute
    {
        get { Simple.GetMute(out var m); return m; }
        set => Simple.SetMute(value, CoreAudioCom.NoContext);
    }

    public void Subscribe(SessionEventsSink sink)
    {
        _control!.RegisterAudioSessionNotification(sink);
        _events = sink;
    }

    public void Unsubscribe()
    {
        var events = _events;
        _events = null;
        if (events is not null) _control?.UnregisterAudioSessionNotification(events);
    }

    public void Dispose()
    {
        CoreAudioCom.Release(_control);
        _control = null;
    }
}

// ---- Receptores dos avisos do Windows (threads de fundo): só repassam os valores ----

[GeneratedComClass]
internal sealed partial class DeviceNotificationSink : IMMNotificationClient
{
    /// <summary>Saída padrão mudou: fluxo (0 = Render), papel (1 = Multimedia) e id (pode ser nulo).</summary>
    public event Action<int, int, string?>? DefaultDeviceChanged;
    public event Action? DeviceAdded;
    public event Action? DeviceRemoved;
    public event Action? DeviceStateChanged;

    /// <summary>Depois de chamado, os avisos que ainda chegarem são ignorados.</summary>
    public void Detach()
    {
        DefaultDeviceChanged = null;
        DeviceAdded = null;
        DeviceRemoved = null;
        DeviceStateChanged = null;
    }

    public void OnDeviceStateChanged(string deviceId, int newState) => DeviceStateChanged?.Invoke();
    public void OnDeviceAdded(string deviceId) => DeviceAdded?.Invoke();
    public void OnDeviceRemoved(string deviceId) => DeviceRemoved?.Invoke();
    public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId) => DefaultDeviceChanged?.Invoke(flow, role, defaultDeviceId);
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { } // dispara muitas vezes: ignorado (D3 da Fase 3)
}

[GeneratedComClass]
internal sealed partial class EndpointVolumeSink : IAudioEndpointVolumeCallback
{
    private volatile Action<float, bool>? _onChanged;

    public EndpointVolumeSink(Action<float, bool> onChanged)
    {
        _onChanged = onChanged;
    }

    public void OnNotify(IntPtr notifyData)
    {
        if (notifyData == IntPtr.Zero) return;
        var (volume, muted) = CoreAudioValues.ReadVolumeNotification(notifyData);
        _onChanged?.Invoke(volume, muted);
    }
}

[GeneratedComClass]
internal sealed partial class SessionNotificationSink : IAudioSessionNotification
{
    private volatile Action? _onCreated;

    public SessionNotificationSink(Action onCreated)
    {
        _onCreated = onCreated;
    }

    // O Windows entrega a sessão nova, mas o nosso monitor relê a lista na thread da tela (D4 da Fase 2).
    public void OnSessionCreated(IntPtr newSession) => _onCreated?.Invoke();
}

[GeneratedComClass]
internal sealed partial class SessionEventsSink : IAudioSessionEvents
{
    private readonly Action<float, bool> _onVolume;
    private readonly Action _onName;
    private readonly Action<int> _onState;
    private readonly Action _onDisconnected;

    public SessionEventsSink(Action<float, bool> onVolume, Action onName, Action<int> onState, Action onDisconnected)
    {
        _onVolume = onVolume;
        _onName = onName;
        _onState = onState;
        _onDisconnected = onDisconnected;
    }

    public void OnDisplayNameChanged(string newDisplayName, in Guid eventContext) => _onName();
    public void OnIconPathChanged(string newIconPath, in Guid eventContext) { }
    public void OnSimpleVolumeChanged(float newVolume, bool newMute, in Guid eventContext) => _onVolume(newVolume, newMute);
    public void OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, in Guid eventContext) { }
    public void OnGroupingParamChanged(in Guid newGroupingParam, in Guid eventContext) { }
    public void OnStateChanged(int newState) => _onState(newState);
    public void OnSessionDisconnected(int disconnectReason) => _onDisconnected();
}
