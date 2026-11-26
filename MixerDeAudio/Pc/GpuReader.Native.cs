using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MixerDeAudio.Pc;

/// <summary>
/// Lê a GPU em % pelo contador do Windows "GPU Engine / Utilization Percentage" (PDH, sem pacote novo; D13 da Fase 15).
/// Só existe com o <c>info</c> assinado (criado e descartado junto com o <see cref="PcInfoReader"/>). A conta fica em
/// <see cref="PcInfoRules.GpuPercent"/>. Qualquer falha dá "sem leitura" (nulo): a GPU nunca derruba o estado do PC.
/// </summary>
internal sealed partial class GpuReader : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhMoreData = 0x800007D2;
    private const string CounterPath = @"\GPU Engine(*)\Utilization Percentage";

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);

    private nint _query;
    private nint _counter;
    private bool _failed;

    /// <summary>Uma leitura (0 a 100) ou nulo se o Windows não tem o contador, ainda não há duas amostras ou algo falhou. A primeira só prepara a amostra.</summary>
    public int? Read()
    {
        if (_failed) return null;
        try
        {
            if (_query == 0)
            {
                if (!Open()) _failed = true;
                return null; // a primeira só prepara a amostra
            }
            if (PdhCollectQueryData(_query) != 0) return null;

            uint size = 0;
            var status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out var count, 0);
            if (status != PdhMoreData || size == 0) return null; // ainda sem segunda amostra, ou sem motores

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble, ref size, out count, buffer);
                if (status != 0) return null;
                var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_W>();
                var list = new List<KeyValuePair<string, double>>((int)count);
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_W>(buffer + i * itemSize);
                    if (item.Status != 0 || item.Name == IntPtr.Zero) continue; // 0 = PDH_CSTATUS_VALID_DATA
                    var name = Marshal.PtrToStringUni(item.Name);
                    if (name is not null) list.Add(new KeyValuePair<string, double>(name, item.Value));
                }
                return PcInfoRules.GpuPercent(list);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ArgumentException or InvalidOperationException)
        {
            _failed = true;
            return null;
        }
    }

    private bool Open()
    {
        if (PdhOpenQueryW(null, 0, out _query) != 0) { _query = 0; return false; }
        if (PdhAddEnglishCounterW(_query, CounterPath, 0, out _counter) != 0)
        {
            PdhCloseQuery(_query);
            _query = 0;
            return false;
        }
        PdhCollectQueryData(_query); // amostra inicial: o contador é uma taxa e precisa de duas
        return true;
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            try { PdhCloseQuery(_query); } catch (DllNotFoundException) { }
            _query = 0;
        }
    }
}
