using System;
using System.Collections.Generic;

namespace MixerDeAudio.Pc;

/// <summary>Modo de projeção, o mesmo do Win+P (Fase 9, D9).</summary>
public enum ProjectionMode
{
    /// <summary>Só a tela do PC (a principal).</summary>
    Primary,
    /// <summary>Só a segunda tela.</summary>
    Second,
    Extend,
    Duplicate,
}

/// <summary>Um caminho de vídeo do <c>QDC_ALL_PATHS</c>: o alvo (monitor) e se ele está disponível.</summary>
public readonly record struct DisplayTarget(long AdapterLuid, uint TargetId, bool Available);

/// <summary>O que o celular mostra da projeção: modo atual (nulo se o Windows não disse) e quantas telas.</summary>
public readonly record struct ProjectionInfo(ProjectionMode? Mode, int Screens);

/// <summary>Projeção e contagem de telas (Fase 9, D9). Aqui só a parte pura; a chamada ao Windows fica em <c>DisplayTopology.Native.cs</c>.</summary>
public static partial class DisplayTopology
{
    // Valores de SDC_TOPOLOGY_* (iguais aos de DISPLAYCONFIG_TOPOLOGY_ID).
    public const uint TopologyInternal = 0x1;
    public const uint TopologyClone = 0x2;
    public const uint TopologyExtend = 0x4;
    public const uint TopologyExternal = 0x8;

    public const int PathInfoSize = 72;

    public static bool TryParseMode(string? id, out ProjectionMode mode)
    {
        switch (id)
        {
            case "principal": mode = ProjectionMode.Primary; return true;
            case "segunda": mode = ProjectionMode.Second; return true;
            case "estender": mode = ProjectionMode.Extend; return true;
            case "duplicar": mode = ProjectionMode.Duplicate; return true;
            default: mode = default; return false;
        }
    }

    public static string Id(ProjectionMode mode) => mode switch
    {
        ProjectionMode.Primary => "principal",
        ProjectionMode.Second => "segunda",
        ProjectionMode.Extend => "estender",
        _ => "duplicar",
    };

    public static uint ToFlag(ProjectionMode mode) => mode switch
    {
        ProjectionMode.Primary => TopologyInternal,
        ProjectionMode.Second => TopologyExternal,
        ProjectionMode.Extend => TopologyExtend,
        _ => TopologyClone,
    };

    public static ProjectionMode? FromFlag(uint flag) => flag switch
    {
        TopologyInternal => ProjectionMode.Primary,
        TopologyExternal => ProjectionMode.Second,
        TopologyExtend => ProjectionMode.Extend,
        TopologyClone => ProjectionMode.Duplicate,
        _ => null,
    };

    /// <summary>Quantas telas estão ligadas ao PC: alvos distintos (adaptador + id) disponíveis.</summary>
    public static int CountScreens(IEnumerable<DisplayTarget> targets)
    {
        var seen = new HashSet<(long, uint)>();
        foreach (var t in targets)
            if (t.Available) seen.Add((t.AdapterLuid, t.TargetId));
        return seen.Count;
    }

    /// <summary>
    /// Lê os alvos de um buffer de <c>DISPLAYCONFIG_PATH_INFO</c> (72 bytes cada): o alvo começa no byte 20
    /// (adaptador 8 bytes, id 4) e <c>targetAvailable</c> fica no byte 60.
    /// </summary>
    public static List<DisplayTarget> ParsePaths(ReadOnlySpan<byte> buffer, int count)
    {
        var list = new List<DisplayTarget>(count);
        for (var i = 0; i < count; i++)
        {
            var at = i * PathInfoSize;
            if (at + PathInfoSize > buffer.Length) break;
            var luid = BitConverter.ToInt64(buffer.Slice(at + 20, 8));
            var id = BitConverter.ToUInt32(buffer.Slice(at + 28, 4));
            var available = BitConverter.ToInt32(buffer.Slice(at + 60, 4)) != 0;
            list.Add(new DisplayTarget(luid, id, available));
        }
        return list;
    }
}
