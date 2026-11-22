using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MixerDeAudio.Pc;

/// <summary>Lê o estado do PC (Fase 9d, D27): CPU, memória, tempo ligado, bateria e disco. Só existe com a aba PC aberta.</summary>
internal sealed partial class PcInfoReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    private CpuTimes _previous;
    private bool _hasPrevious;

    /// <summary>Uma leitura. A primeira leva a média desde que o PC ligou; as seguintes, o intervalo desde a anterior.</summary>
    public PcInfoSnapshot Read()
    {
        var times = default(CpuTimes);
        if (GetSystemTimes(out var idle, out var kernel, out var user)) times = new CpuTimes(idle.Value, kernel.Value, user.Value);
        var cpu = PcInfoRules.CpuPercent(_hasPrevious ? _previous : default, times);
        _previous = times;
        _hasPrevious = true;

        long used = 0, total = 0;
        var mem = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            total = (long)mem.TotalPhys;
            used = total - (long)mem.AvailPhys;
        }

        int? battery = null;
        var charging = false;
        if (GetSystemPowerStatus(out var power))
        {
            battery = PcInfoRules.BatteryPercent(power.BatteryFlag, power.BatteryLifePercent);
            charging = battery is not null && PcInfoRules.IsCharging(power.BatteryFlag);
        }

        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        DiskSpace.TryGet(root, out var free, out var size);

        return new PcInfoSnapshot(cpu, used, total, Environment.TickCount64 / 1000, battery, charging, free, size);
    }
}

/// <summary>Espaço do disco pelo Windows (<c>GetDiskFreeSpaceExW</c>); sem <c>DriveInfo</c>, que o corte do publicado deixa de fora.</summary>
internal static partial class DiskSpace
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string? directory, out ulong freeToCaller, out ulong total, out ulong totalFree);

    /// <summary>Espaço livre para o usuário e total do disco que contém <paramref name="path"/>; falso se o Windows não respondeu.</summary>
    public static bool TryGet(string? path, out long free, out long total)
    {
        free = total = 0;
        if (string.IsNullOrEmpty(path) || !GetDiskFreeSpaceEx(path, out var f, out var t, out _)) return false;
        free = (long)f;
        total = (long)t;
        return true;
    }
}
