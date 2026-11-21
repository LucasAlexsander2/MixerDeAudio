using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace MixerDeAudio.Audio.CoreAudio;

// Interfaces do Core Audio do Windows (Fase 8, D8), geradas na compilação. O que vale é a posição na tabela de métodos:
// os métodos que o código não usa entram só para ocupar o lugar, com tipos simples. Os que podem devolver um HRESULT de
// "não achou" ficam com PreserveSig. Texto devolvido pelo Windows (LPWSTR) é liberado com CoTaskMemFree pelo marshaller.

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice(string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    void Activate(in Guid iid, int clsCtx, IntPtr activationParams, out IntPtr result);
    void OpenPropertyStore(int access, out IPropertyStore properties);
    void GetId(out string id);
    void GetState(out int state);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    void GetCount(out uint count);
    void Item(uint index, out IMMDevice device);
}

[GeneratedComInterface]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
internal partial interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(in PropertyKey key, out PropVariant value);
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, in Guid eventContext);
    void SetMasterVolumeLevelScalar(float level, in Guid eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    void GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, in Guid eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, in Guid eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[GeneratedComInterface]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig] int GetAudioSessionControl(IntPtr groupingId, uint flags, out IntPtr control);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr groupingId, uint flags, out IntPtr volume);
    // IAudioSessionManager2
    void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    void RegisterSessionNotification(IAudioSessionNotification notification);
    void UnregisterSessionNotification(IAudioSessionNotification notification);
}

[GeneratedComInterface]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl2 session);
}

/// <summary><c>IAudioSessionControl2</c> já com os 9 métodos do <c>IAudioSessionControl</c> que vêm antes na tabela.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
internal partial interface IAudioSessionControl2
{
    void GetState(out int state);
    void GetDisplayName(out string name);
    [PreserveSig] int SetDisplayName(IntPtr name, IntPtr eventContext);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(IntPtr path, IntPtr eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingId);
    [PreserveSig] int SetGroupingParam(IntPtr groupingId, IntPtr eventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents events);
    void UnregisterAudioSessionNotification(IAudioSessionEvents events);
    // IAudioSessionControl2
    void GetSessionIdentifier(out string id);
    void GetSessionInstanceIdentifier(out string id);
    void GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession(); // S_OK (0) = é a sessão dos sons do sistema; S_FALSE (1) = não é
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[GeneratedComInterface]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal partial interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, in Guid eventContext);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, in Guid eventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

// ---- Callbacks (implementados por nós, chamados pelo Windows em threads de fundo) ----

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IMMNotificationClient
{
    void OnDeviceStateChanged(string deviceId, int newState);
    void OnDeviceAdded(string deviceId);
    void OnDeviceRemoved(string deviceId);
    void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId);
    void OnPropertyValueChanged(string deviceId, PropertyKey key);
}

[GeneratedComInterface]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
internal partial interface IAudioEndpointVolumeCallback
{
    void OnNotify(IntPtr notifyData);
}

[GeneratedComInterface]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
internal partial interface IAudioSessionNotification
{
    void OnSessionCreated(IntPtr newSession);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
internal partial interface IAudioSessionEvents
{
    void OnDisplayNameChanged(string newDisplayName, in Guid eventContext);
    void OnIconPathChanged(string newIconPath, in Guid eventContext);
    void OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, in Guid eventContext);
    void OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, in Guid eventContext);
    void OnGroupingParamChanged(in Guid newGroupingParam, in Guid eventContext);
    void OnStateChanged(int newState);
    void OnSessionDisconnected(int disconnectReason);
}
