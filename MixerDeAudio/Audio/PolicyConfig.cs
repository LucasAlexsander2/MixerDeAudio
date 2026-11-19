using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using MixerDeAudio.Services;

namespace MixerDeAudio.Audio;

/// <summary>
/// Interface não documentada <c>IPolicyConfig</c> (D9), estável desde o Windows 7 e usada pelo EarTrumpet e pelo
/// SoundSwitch. Só <c>SetDefaultEndpoint</c> é usado, mas todos os métodos antes dele precisam estar, na ordem certa,
/// porque o que vale é a posição na tabela de métodos. Gerada na compilação (<c>GeneratedComInterface</c>).
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
internal partial interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat(string deviceId, IntPtr format);
    [PreserveSig] int GetDeviceFormat(string deviceId, int isDefault, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(string deviceId);
    [PreserveSig] int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod(string deviceId, IntPtr period);
    [PreserveSig] int GetShareMode(string deviceId, IntPtr mode);
    [PreserveSig] int SetShareMode(string deviceId, IntPtr mode);
    [PreserveSig] int GetPropertyValue(string deviceId, int store, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(string deviceId, int store, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint(string deviceId, int role);
    [PreserveSig] int SetEndpointVisibility(string deviceId, int visible);
}

/// <summary>Troca a saída padrão do Windows (D10): papéis Console, Multimedia e Communications (P2).</summary>
internal static partial class PolicyConfig
{
    private static readonly Guid ClassId = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    private static readonly Guid InterfaceId = new("f8679f50-850a-41cf-9c72-430f290290c8");
    private const int ClsctxAll = 23; // INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid, out IntPtr instance);

    /// <summary>Torna a saída a padrão para os três papéis. Lança exceção se o Windows recusar.</summary>
    public static void SetDefault(string deviceId)
    {
        var hr = CoCreateInstance(ClassId, IntPtr.Zero, ClsctxAll, InterfaceId, out var ptr);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var policy = (IPolicyConfig)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            // 0 = Console, 1 = Multimedia, 2 = Communications
            for (var role = 0; role < 3; role++)
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }
}
