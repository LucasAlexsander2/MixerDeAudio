using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MixerDeAudio.Interop;

namespace MixerDeAudio.Pc;

/// <summary>
/// Brilho do monitor por DDC/CI (Fase 12, D1 e D9), pela <c>dxva2.dll</c>. Os handles dos monitores físicos são abertos e
/// soltos a cada operação. Cada <c>HMONITOR</c> é ligado ao índice da tela pela mesma ordem de
/// <c>ScreenCapture.Monitors()</c>. Lento (40 a 100 ms por comando): chamar só pelo <see cref="BrightnessWorker"/>.
/// </summary>
internal static unsafe partial class Brightness
{
    private const int NameChars = 128;

    // PHYSICAL_MONITOR: HANDLE + WCHAR[128]; só o handle importa
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        public fixed char Description[NameChars];
    }

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, PhysicalMonitor* monitors);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorBrightness(IntPtr physical, out uint min, out uint current, out uint max);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMonitorBrightness(IntPtr physical, uint value);

    [LibraryImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyPhysicalMonitors(uint count, PhysicalMonitor* monitors);

    private readonly record struct Hm(IntPtr Handle, int Left, int Top, bool Primary);

    /// <summary>Os HMONITOR na ordem das telas (principal primeiro, depois da esquerda para a direita).</summary>
    private static List<IntPtr> OrderedHandles()
    {
        var raw = new List<Hm>();
        var handle = GCHandle.Alloc(raw);
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &EnumProc, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        // mesma regra de ScreenRules.Order: principal primeiro, depois Left e Top
        raw.Sort((a, b) =>
        {
            if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
            var c = a.Left.CompareTo(b.Left);
            return c != 0 ? c : a.Top.CompareTo(b.Top);
        });
        var list = new List<IntPtr>(raw.Count);
        foreach (var h in raw) list.Add(h.Handle);
        return list;
    }

    [UnmanagedCallersOnly]
    private static int EnumProc(IntPtr monitor, IntPtr hdc, NativeMethods.RECT* rect, IntPtr data)
    {
        try
        {
            var list = (List<Hm>)GCHandle.FromIntPtr(data).Target!;
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)sizeof(NativeMethods.MONITORINFO) };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
                list.Add(new Hm(monitor, info.rcMonitor.Left, info.rcMonitor.Top, (info.dwFlags & 1) != 0));
        }
        catch
        {
            // um callback nativo não pode lançar: essa tela fica de fora
        }
        return 1;
    }

    /// <summary>Abre os monitores físicos de um HMONITOR; vazio se não há. Quem chama solta com <see cref="Release"/>.</summary>
    private static PhysicalMonitor[] Open(IntPtr monitor)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var n) || n == 0) return Array.Empty<PhysicalMonitor>();
        var arr = new PhysicalMonitor[n];
        fixed (PhysicalMonitor* p = arr)
        {
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, n, p)) return Array.Empty<PhysicalMonitor>();
        }
        return arr;
    }

    private static void Release(PhysicalMonitor[] arr)
    {
        if (arr.Length == 0) return;
        fixed (PhysicalMonitor* p = arr) DestroyPhysicalMonitors((uint)arr.Length, p);
    }

    /// <summary>Brilho atual (0 a 100) de cada tela; nulo onde a leitura falhou (sem DDC/CI).</summary>
    public static IReadOnlyList<int?> Read()
    {
        var result = new List<int?>();
        foreach (var hm in OrderedHandles())
        {
            int? pct = null;
            var phys = Open(hm);
            try
            {
                foreach (var m in phys)
                {
                    if (GetMonitorBrightness(m.Handle, out var min, out var cur, out var max))
                    {
                        pct = BrightnessRules.ToPercent((int)cur, (int)min, (int)max);
                        break;
                    }
                }
            }
            finally
            {
                Release(phys);
            }
            result.Add(pct);
        }
        return result;
    }

    /// <summary>Muda o brilho da tela <paramref name="tela"/> (0 a 100). Falso se a tela não existe ou o monitor recusou.</summary>
    public static bool Set(int tela, int pct)
    {
        var handles = OrderedHandles();
        if (tela < 0 || tela >= handles.Count) return false;
        var phys = Open(handles[tela]);
        var ok = false;
        try
        {
            foreach (var m in phys)
            {
                if (!GetMonitorBrightness(m.Handle, out var min, out _, out var max)) continue;
                if (SetMonitorBrightness(m.Handle, (uint)BrightnessRules.ToRaw(pct, (int)min, (int)max))) ok = true;
            }
        }
        finally
        {
            Release(phys);
        }
        return ok;
    }
}
