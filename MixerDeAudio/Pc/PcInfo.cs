using System;

namespace MixerDeAudio.Pc;

/// <summary>Tempos acumulados do <c>GetSystemTimes</c> (o <c>Kernel</c> já inclui o <c>Idle</c>).</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>Uma leitura do estado do PC (Fase 9d, D27). <c>Battery</c> nulo = sem bateria.</summary>
public sealed record PcInfoSnapshot(int Cpu, long MemUsed, long MemTotal, long UptimeSeconds, int? Battery, bool Charging, long DiskFree, long DiskTotal);

/// <summary>Contas puras do estado do PC (Fase 9d, D27).</summary>
public static class PcInfoRules
{
    /// <summary>CPU em % (0 a 100) entre duas leituras. Sem intervalo (ou contador que voltou) = 0.</summary>
    public static int CpuPercent(CpuTimes previous, CpuTimes now)
    {
        if (now.Kernel < previous.Kernel || now.User < previous.User || now.Idle < previous.Idle) return 0;
        var total = (now.Kernel - previous.Kernel) + (now.User - previous.User);
        if (total == 0) return 0;
        var idle = now.Idle - previous.Idle;
        if (idle >= total) return 0;
        var busy = total - idle;
        return (int)Math.Clamp(Math.Round(busy * 100.0 / total), 0, 100);
    }

    /// <summary>Bateria em %: nulo se o PC não tem bateria (flag 128) ou o Windows não sabe (flag 255 ou % 255).</summary>
    public static int? BatteryPercent(byte flag, byte percent)
    {
        if (flag == 255 || (flag & 128) != 0 || percent > 100) return null;
        return percent;
    }

    public static bool IsCharging(byte flag) => flag != 255 && (flag & 8) != 0;
}
