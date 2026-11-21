using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using MixerDeAudio.Interop;

namespace MixerDeAudio.Audio;

[StructLayout(LayoutKind.Sequential)]
internal struct ShellPropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

/// <summary>
/// <c>IShellItem2</c> inteira, na ordem da tabela de métodos (só <c>GetString</c> é usado). Gerada na compilação, como o
/// <c>IPolicyConfig</c>.
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
internal partial interface IShellItem2
{
    [PreserveSig] int BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr result);
    [PreserveSig] int GetParent(out IntPtr parent);
    [PreserveSig] int GetDisplayName(int nameType, out IntPtr name);
    [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    [PreserveSig] int Compare(IntPtr other, uint hint, out int order);
    [PreserveSig] int GetPropertyStore(int flags, in Guid riid, out IntPtr store);
    [PreserveSig] int GetPropertyStoreWithCreateObject(int flags, IntPtr createObject, in Guid riid, out IntPtr store);
    [PreserveSig] int GetPropertyStoreForKeys(IntPtr keys, uint count, int flags, in Guid riid, out IntPtr store);
    [PreserveSig] int GetPropertyDescriptionList(in ShellPropertyKey key, in Guid riid, out IntPtr list);
    [PreserveSig] int Update(IntPtr bindContext);
    [PreserveSig] int GetProperty(in ShellPropertyKey key, IntPtr value);
    [PreserveSig] int GetCLSID(in ShellPropertyKey key, out Guid clsid);
    [PreserveSig] int GetFileTime(in ShellPropertyKey key, out long fileTime);
    [PreserveSig] int GetInt32(in ShellPropertyKey key, out int value);
    [PreserveSig] int GetString(in ShellPropertyKey key, out string value);
}

/// <summary>
/// Parte impura da ligação mídia-linha (Fase 7, D4): AUMID para nome amigável e .exe pela pasta de apps do Windows, e AUMID
/// de um processo (apps da Loja). Uma instância só para o app inteiro; os caches de AUMID e .exe sobrevivem entre aberturas (o .exe é conferido com File.Exists); falha deixa o campo nulo, sem
/// erro no log. Só na thread da tela (a pasta de apps pede STA).
/// </summary>
internal sealed class AumidResolver
{
    public readonly record struct ShellData(string? Name, string? ExePath);

    private static readonly Guid AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    private static readonly Guid ShellItem2Id = new("7e9fb0d3-919f-4307-ab2e-9b1860310c93");
    private static readonly ShellPropertyKey TargetParsingPath = new() { FormatId = new Guid("b9b4b3fc-2b51-4a42-b5d8-324146afcf25"), PropertyId = 2 };
    private static readonly ShellPropertyKey ItemNameDisplay = new() { FormatId = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), PropertyId = 10 };

    internal static readonly StrategyBasedComWrappers Wrappers = new(); // um só para o processo (também usado pelo AppIconCache)

    private readonly Dictionary<string, ShellData> _shell = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string?> _byPid = new();
    private readonly Dictionary<string, string?> _storeExe = new(StringComparer.OrdinalIgnoreCase);

    public ShellData ShellResolve(string aumid)
    {
        if (_shell.TryGetValue(aumid, out var cached) && (cached.ExePath is null || File.Exists(cached.ExePath))) return cached;
        var data = ReadShell(aumid);
        _shell[aumid] = data;
        return data;
    }

    /// <summary>AUMID do processo, ou nulo (processo comum, que fechou ou sem acesso). Fica em cache por pid.</summary>
    public string? ProcessAumid(int pid)
    {
        if (pid <= 0) return null;
        if (_byPid.TryGetValue(pid, out var cached)) return cached;
        var aumid = ReadProcessAumid(pid);
        _byPid[pid] = aumid;
        return aumid;
    }

    /// <summary>
    /// .exe de um app da Loja sem linha de som, para o ícone: o do primeiro processo cujo AUMID bate. Uma varredura por
    /// AUMID enquanto o hub está ativo (só AUMID com "!").
    /// </summary>
    public string? StoreExePath(string aumid)
    {
        if (!aumid.Contains('!')) return null;
        if (_storeExe.TryGetValue(aumid, out var cached) && (cached is null || File.Exists(cached))) return cached;
        string? found = null;
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (found is null && string.Equals(ReadProcessAumid(p.Id), aumid, StringComparison.OrdinalIgnoreCase))
                        found = QueryPath(p.Id);
                }
                catch { /* processo que fechou: segue */ }
                finally { p.Dispose(); }
            }
        }
        catch { /* sem acesso à lista: sem ícone */ }
        _storeExe[aumid] = found;
        return found;
    }

    /// <summary>Só o cache por pid: o Windows reaproveita pid. Os outros caches valem o app inteiro.</summary>
    public void ClearPids() => _byPid.Clear();

    private static ShellData ReadShell(string aumid)
    {
        var ptr = IntPtr.Zero;
        try
        {
            if (NativeMethods.SHCreateItemInKnownFolder(AppsFolder, 0, aumid, ShellItem2Id, out ptr) != 0 || ptr == IntPtr.Zero)
                return default;
            var item = (IShellItem2)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            return new ShellData(GetString(item, ItemNameDisplay), GetString(item, TargetParsingPath));
        }
        catch
        {
            return default;
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.Release(ptr);
        }
    }

    private static string? GetString(IShellItem2 item, ShellPropertyKey key)
    {
        try
        {
            if (item.GetString(key, out var value) != 0) return null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadProcessAumid(int pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[256];
            var length = (uint)buffer.Length;
            return NativeMethods.GetApplicationUserModelId(handle, ref length, buffer) == 0 && length > 1
                ? new string(buffer, 0, (int)length - 1)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string? QueryPath(int pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
