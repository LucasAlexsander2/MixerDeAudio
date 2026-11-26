using System;
using System.Collections.Generic;

namespace MixerDeAudio.Pc;

/// <summary>Tempos acumulados do <c>GetSystemTimes</c> (o <c>Kernel</c> já inclui o <c>Idle</c>).</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>Uma leitura do estado do PC (Fase 9d, D27). <c>Battery</c> nulo = sem bateria; <c>Gpu</c> nulo = sem leitura (D13 da Fase 15).</summary>
public sealed record PcInfoSnapshot(int Cpu, long MemUsed, long MemTotal, long UptimeSeconds, int? Battery, bool Charging, long DiskFree, long DiskTotal, int? Gpu = null);

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

    /// <summary>
    /// GPU em % (0 a 100) a partir das instâncias do contador "GPU Engine / Utilization Percentage" (D13 da Fase 15): soma os motores
    /// <c>engtype_3D</c> de cada adaptador (<c>luid_</c> no nome da instância) e vale o adaptador mais usado, cortado em 100.
    /// Sem nenhum motor 3D (lista vazia ou valores inválidos) = nulo (sem leitura).
    /// </summary>
    public static int? GpuPercent(IEnumerable<KeyValuePair<string, double>> instances)
    {
        Dictionary<string, double>? perAdapter = null;
        foreach (var (name, value) in instances)
        {
            if (name is null || double.IsNaN(value) || double.IsInfinity(value)) continue;
            if (!name.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
            perAdapter ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var luid = GpuAdapterId(name);
            perAdapter[luid] = perAdapter.GetValueOrDefault(luid) + Math.Max(0, value);
        }
        if (perAdapter is null) return null;
        var best = 0.0;
        foreach (var total in perAdapter.Values) best = Math.Max(best, total);
        return (int)Math.Clamp(Math.Round(best), 0, 100);
    }

    /// <summary>O trecho <c>luid_0x..._0x...</c> do nome da instância (ex.: <c>pid_12_luid_0x0_0xAB_phys_0_eng_0_engtype_3D</c>); sem ele, tudo conta como um adaptador só.</summary>
    internal static string GpuAdapterId(string instance)
    {
        var start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "";
        var end = instance.IndexOf("_phys", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? instance[start..] : instance[start..end];
    }
}
