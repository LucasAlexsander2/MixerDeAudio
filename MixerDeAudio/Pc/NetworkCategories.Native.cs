using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using MixerDeAudio.Audio;
using MixerDeAudio.Audio.CoreAudio;

namespace MixerDeAudio.Pc;

// Fase 16, D5: leitura da categoria de cada rede (Pública, Privada, Domínio) pelo Gerenciador de Listas de Rede do Windows
// (netprofm), sem pacote novo. As interfaces são do tipo "dual" (IDispatch): os quatro primeiros métodos de cada uma só
// ocupam o lugar na tabela. Os métodos que o código não usa entram só para manter a ordem.

[GeneratedComInterface]
[Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetworkListManager
{
    [PreserveSig] int GetTypeInfoCount(out uint count);
    [PreserveSig] int GetTypeInfo(uint index, uint lcid, out IntPtr info);
    [PreserveSig] int GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [PreserveSig] int Invoke(int dispId, IntPtr riid, uint lcid, ushort flags, IntPtr parameters, IntPtr result, IntPtr exception, IntPtr argErr);
    [PreserveSig] int GetNetworks(int flags, out IEnumNetworks networks);
    [PreserveSig] int GetNetwork(in Guid id, out INetwork network);
    [PreserveSig] int GetNetworkConnections(out IEnumNetworkConnections connections);
    [PreserveSig] int GetNetworkConnection(in Guid id, out INetworkConnection connection);
    [PreserveSig] int IsConnectedToInternet(out int connected);
    [PreserveSig] int IsConnected(out int connected);
    [PreserveSig] int GetConnectivity(out int connectivity);
}

[GeneratedComInterface]
[Guid("DCB00003-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface IEnumNetworks
{
    [PreserveSig] int GetTypeInfoCount(out uint count);
    [PreserveSig] int GetTypeInfo(uint index, uint lcid, out IntPtr info);
    [PreserveSig] int GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [PreserveSig] int Invoke(int dispId, IntPtr riid, uint lcid, ushort flags, IntPtr parameters, IntPtr result, IntPtr exception, IntPtr argErr);
    [PreserveSig] int GetNewEnum(out IntPtr enumerator);
    [PreserveSig] int Next(uint count, out INetwork network, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
}

[GeneratedComInterface]
[Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetwork
{
    [PreserveSig] int GetTypeInfoCount(out uint count);
    [PreserveSig] int GetTypeInfo(uint index, uint lcid, out IntPtr info);
    [PreserveSig] int GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [PreserveSig] int Invoke(int dispId, IntPtr riid, uint lcid, ushort flags, IntPtr parameters, IntPtr result, IntPtr exception, IntPtr argErr);
    [PreserveSig] int GetName(out IntPtr name);
    [PreserveSig] int SetName(IntPtr name);
    [PreserveSig] int GetDescription(out IntPtr description);
    [PreserveSig] int SetDescription(IntPtr description);
    [PreserveSig] int GetNetworkId(out Guid id);
    [PreserveSig] int GetDomainType(out int type);
    [PreserveSig] int GetNetworkConnections(out IEnumNetworkConnections connections);
    [PreserveSig] int GetTimeCreatedAndConnected(IntPtr lowCreated, IntPtr highCreated, IntPtr lowConnected, IntPtr highConnected);
    [PreserveSig] int IsConnectedToInternet(out int connected);
    [PreserveSig] int IsConnected(out int connected);
    [PreserveSig] int GetConnectivity(out int connectivity);
    [PreserveSig] int GetCategory(out int category);
    [PreserveSig] int SetCategory(int category);
}

[GeneratedComInterface]
[Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface IEnumNetworkConnections
{
    [PreserveSig] int GetTypeInfoCount(out uint count);
    [PreserveSig] int GetTypeInfo(uint index, uint lcid, out IntPtr info);
    [PreserveSig] int GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [PreserveSig] int Invoke(int dispId, IntPtr riid, uint lcid, ushort flags, IntPtr parameters, IntPtr result, IntPtr exception, IntPtr argErr);
    [PreserveSig] int GetNewEnum(out IntPtr enumerator);
    [PreserveSig] int Next(uint count, out INetworkConnection connection, out uint fetched);
    [PreserveSig] int Skip(uint count);
    [PreserveSig] int Reset();
}

[GeneratedComInterface]
[Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B")]
internal partial interface INetworkConnection
{
    [PreserveSig] int GetTypeInfoCount(out uint count);
    [PreserveSig] int GetTypeInfo(uint index, uint lcid, out IntPtr info);
    [PreserveSig] int GetIDsOfNames(IntPtr riid, IntPtr names, uint count, uint lcid, IntPtr ids);
    [PreserveSig] int Invoke(int dispId, IntPtr riid, uint lcid, ushort flags, IntPtr parameters, IntPtr result, IntPtr exception, IntPtr argErr);
    [PreserveSig] int GetNetwork(out INetwork network);
    [PreserveSig] int IsConnectedToInternet(out int connected);
    [PreserveSig] int IsConnected(out int connected);
    [PreserveSig] int GetConnectivity(out int connectivity);
    [PreserveSig] int GetConnectionId(out Guid id);
    [PreserveSig] int GetAdapterId(out Guid id);
    [PreserveSig] int GetDomainType(out int type);
}

/// <summary>Categoria de rede de cada adaptador (chave: o GUID do adaptador, igual ao <c>NetworkInterface.Id</c>).</summary>
internal static partial class NetworkCategories
{
    private const int ClsctxAll = 23;
    private const int EnumConnected = 1; // NLM_ENUM_NETWORK_CONNECTED
    private static readonly Guid ManagerClassId = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private static readonly Guid ManagerInterfaceId = new("DCB00000-570F-4A9B-8D69-199FDBA5723B");

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid, out IntPtr instance);

    /// <summary>
    /// Lê as redes conectadas e a categoria de cada adaptador. Nulo (e <paramref name="error"/> preenchido) se o Windows
    /// recusar: quem chama trata como Privada e registra no log (D5).
    /// </summary>
    public static IReadOnlyDictionary<Guid, NetworkCategory>? Read(out string? error)
    {
        error = null;
        INetworkListManager? manager = null;
        IEnumNetworks? networks = null;
        try
        {
            var hr = CoCreateInstance(ManagerClassId, IntPtr.Zero, ClsctxAll, ManagerInterfaceId, out var ptr);
            if (hr < 0) { error = $"CoCreateInstance 0x{hr:X8}"; return null; }
            try { manager = (INetworkListManager)AumidResolver.Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None); }
            finally { Marshal.Release(ptr); }

            hr = manager.GetNetworks(EnumConnected, out networks);
            if (hr < 0) { error = $"GetNetworks 0x{hr:X8}"; return null; }

            var result = new Dictionary<Guid, NetworkCategory>();
            while (true)
            {
                hr = networks.Next(1, out var network, out var fetched);
                if (hr != 0 || fetched == 0 || network is null) break;
                try { ReadNetwork(network, result); }
                finally { CoreAudioCom.Release(network); }
            }
            return result;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
        finally
        {
            CoreAudioCom.Release(networks);
            CoreAudioCom.Release(manager);
        }
    }

    private static void ReadNetwork(INetwork network, Dictionary<Guid, NetworkCategory> result)
    {
        if (network.GetCategory(out var raw) < 0) return;
        var category = NetworkRules.CategoryFromNlm(raw);
        if (category is null) return;
        if (network.GetNetworkConnections(out var connections) < 0) return;
        try
        {
            while (true)
            {
                var hr = connections.Next(1, out var connection, out var fetched);
                if (hr != 0 || fetched == 0 || connection is null) break;
                try
                {
                    if (connection.GetAdapterId(out var adapter) < 0) continue;
                    // Mais de uma rede no mesmo adaptador: vale a mais restritiva (Pública antes de tudo)
                    if (!result.TryGetValue(adapter, out var old) || category == NetworkCategory.Public || old == NetworkCategory.Domain)
                        result[adapter] = category.Value;
                }
                finally { CoreAudioCom.Release(connection); }
            }
        }
        finally { CoreAudioCom.Release(connections); }
    }
}
