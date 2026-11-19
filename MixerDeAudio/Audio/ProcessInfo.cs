using System;
using System.Collections.Generic;
using System.Diagnostics;
using MixerDeAudio.Interop;

namespace MixerDeAudio.Audio;

/// <summary>
/// Caminho do .exe, descrição e nome de um processo (D17), com cache por id enquanto a janela está aberta.
/// Só na thread da tela.
/// </summary>
internal sealed class ProcessInfo
{
    public readonly record struct Data(string? ExePath, string? FileDescription, string? ProcessName);

    private readonly Dictionary<int, Data> _cache = new();

    public Data Get(int processId)
    {
        if (processId <= 0) return default;
        if (_cache.TryGetValue(processId, out var cached)) return cached;
        var data = Read(processId);
        _cache[processId] = data;
        return data;
    }

    public void Clear() => _cache.Clear();

    private static Data Read(int processId)
    {
        var path = QueryPath(processId);
        string? description = null;
        string? name = null;
        if (path is not null)
        {
            try { description = FileVersionInfo.GetVersionInfo(path).FileDescription; }
            catch { /* arquivo sem versão ou sem acesso: cai para o nome do processo */ }
        }
        try
        {
            using var p = Process.GetProcessById(processId);
            name = p.ProcessName;
        }
        catch { /* o processo acabou de fechar */ }
        if (name is null && path is not null) name = System.IO.Path.GetFileNameWithoutExtension(path);
        return new Data(path, description, name);
    }

    /// <summary>Caminho completo do .exe, com <c>PROCESS_QUERY_LIMITED_INFORMATION</c> (funciona também com mais privilégio).</summary>
    private static string? QueryPath(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
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

    /// <summary>Resolve um texto de recurso (<c>@dll,-123</c>). Nulo se falhar ou se não for um texto de recurso.</summary>
    public static string? ResolveIndirect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!text.StartsWith('@')) return text;
        try
        {
            var buffer = new char[260];
            if (NativeMethods.SHLoadIndirectString(text, buffer, (uint)buffer.Length, IntPtr.Zero) != 0) return null;
            var s = new string(buffer).TrimEnd('\0');
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }
}
