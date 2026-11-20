using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using MixerDeAudio.Services;

namespace MixerDeAudio.Audio;

/// <summary>
/// Fábrica <c>IAudioPolicyConfigFactory</c> (D1), a mesma interface interna que a tela "Volume do app e preferências
/// de dispositivo" e o EarTrumpet usam. Na tabela de métodos vêm os 3 do <c>IInspectable</c>, 19 métodos que não são
/// usados (só ocupam a posição) e então Set e Get da saída por app. <c>ClearAllPersistedApplicationDefaultEndpoints</c>
/// vem logo depois e NÃO é declarado de propósito: apagaria a escolha de todos os apps do usuário.
/// </summary>
[GeneratedComInterface]
[Guid("ab3d4648-e242-459f-b02f-541c70306324")]
internal partial interface IAudioPolicyConfigFactory
{
    // IInspectable
    [PreserveSig] int GetIids();
    [PreserveSig] int GetRuntimeClassName();
    [PreserveSig] int GetTrustLevel();
    // 19 métodos não usados, na ordem
    [PreserveSig] int AddCtxVolumeChange();
    [PreserveSig] int RemoveCtxVolumeChanged();
    [PreserveSig] int AddRingerVibrateStateChanged();
    [PreserveSig] int RemoveRingerVibrateStateChange();
    [PreserveSig] int SetVolumeGroupGainForId();
    [PreserveSig] int GetVolumeGroupGainForId();
    [PreserveSig] int GetActiveVolumeGroupForEndpointId();
    [PreserveSig] int GetVolumeGroupsForEndpoint();
    [PreserveSig] int GetCurrentVolumeContext();
    [PreserveSig] int SetVolumeGroupMuteForId();
    [PreserveSig] int GetVolumeGroupMuteForId();
    [PreserveSig] int SetRingerVibrateState();
    [PreserveSig] int GetRingerVibrateState();
    [PreserveSig] int SetPreferredChatApplication();
    [PreserveSig] int ResetPreferredChatApplication();
    [PreserveSig] int GetPreferredChatApplication();
    [PreserveSig] int GetCurrentChatApplications();
    [PreserveSig] int AddChatContextChanged();
    [PreserveSig] int RemoveChatContextChanged();
    // Saída por app
    [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr hstringId);
    [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr hstringId);
}

/// <summary>
/// Lê e grava a saída escolhida de um app (D3 e D4). Criado uma vez por abertura da janela, na thread da tela
/// (D5), e liberado no <c>Closed</c>. O Windows guarda a escolha por app; a chamada recebe um pid.
/// </summary>
internal sealed partial class AppRoutingService : IDisposable
{
    private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
    private static readonly StrategyBasedComWrappers Wrappers = new(); // um só para o processo (não cria um por abertura)
    private static readonly Guid FactoryIid = new("ab3d4648-e242-459f-b02f-541c70306324");
    private const int Render = 0;
    private const int RoleConsole = 0;
    private const int RoleMultimedia = 1;

    private IntPtr _ptr;
    private IAudioPolicyConfigFactory? _factory;

    private AppRoutingService(IntPtr ptr, IAudioPolicyConfigFactory factory)
    {
        _ptr = ptr;
        _factory = factory;
    }

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    private static partial IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(IntPtr hstring);

    /// <summary>Cria o serviço; se o Windows não oferecer a interface, registra o erro uma vez e devolve nulo (D5).</summary>
    public static AppRoutingService? TryCreate()
    {
        IntPtr hClass = IntPtr.Zero;
        IntPtr ptr = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(ClassName, ClassName.Length, out hClass));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hClass, FactoryIid, out ptr));
            var factory = (IAudioPolicyConfigFactory)Wrappers
                .GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            var service = new AppRoutingService(ptr, factory);
            ptr = IntPtr.Zero; // o serviço é o dono
            return service;
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO saida por app indisponivel: {ex.Message}");
            if (ptr != IntPtr.Zero) Marshal.Release(ptr);
            return null;
        }
        finally
        {
            if (hClass != IntPtr.Zero) WindowsDeleteString(hClass);
        }
    }

    /// <summary>Id do <c>MMDevice</c> que o app escolheu, ou nulo se ele segue a saída padrão (D3).</summary>
    public string? GetPreferred(int processId)
    {
        var factory = _factory ?? throw new ObjectDisposedException(nameof(AppRoutingService));
        IntPtr h = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(factory.GetPersistedDefaultAudioEndpoint((uint)processId, Render, RoleMultimedia, out h));
            if (h == IntPtr.Zero) return null;
            var buffer = WindowsGetStringRawBuffer(h, out var length);
            if (buffer == IntPtr.Zero || length == 0) return null;
            return AppRouting.FromPolicyId(Marshal.PtrToStringUni(buffer, (int)length));
        }
        finally
        {
            if (h != IntPtr.Zero) WindowsDeleteString(h);
        }
    }

    /// <summary>Primeira escolha encontrada entre os pids do grupo (D4).</summary>
    public string? GetPreferred(IReadOnlyList<int> processIds)
    {
        foreach (var pid in processIds)
        {
            var id = GetPreferred(pid);
            if (id is not null) return id;
        }
        return null;
    }

    /// <summary>Grava a escolha para cada pid (D3 e D4). Com <paramref name="mmDeviceId"/> nulo, apaga a escolha.</summary>
    public void SetPreferred(IReadOnlyList<int> processIds, string? mmDeviceId)
    {
        var factory = _factory ?? throw new ObjectDisposedException(nameof(AppRoutingService));
        var seen = new HashSet<int>();
        foreach (var pid in processIds)
        {
            if (pid <= 0 || !seen.Add(pid)) continue;
            IntPtr h = IntPtr.Zero;
            try
            {
                if (mmDeviceId is not null)
                {
                    var policy = AppRouting.ToPolicyId(mmDeviceId);
                    Marshal.ThrowExceptionForHR(WindowsCreateString(policy, policy.Length, out h));
                }
                // Papéis Console e Multimedia; a comunicação fica com o Windows (D3).
                Marshal.ThrowExceptionForHR(factory.SetPersistedDefaultAudioEndpoint((uint)pid, Render, RoleConsole, h));
                Marshal.ThrowExceptionForHR(factory.SetPersistedDefaultAudioEndpoint((uint)pid, Render, RoleMultimedia, h));
            }
            finally
            {
                if (h != IntPtr.Zero) WindowsDeleteString(h);
            }
        }
    }

    public void Dispose()
    {
        _factory = null;
        if (_ptr != IntPtr.Zero)
        {
            Marshal.Release(_ptr);
            _ptr = IntPtr.Zero;
        }
    }
}
