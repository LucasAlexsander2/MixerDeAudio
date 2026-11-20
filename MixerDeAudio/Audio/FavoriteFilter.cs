using System;
using System.Collections.Generic;

namespace MixerDeAudio.Audio;

/// <summary>Quais saídas aparecem nos botões (D5 e P2). Lógica pura.</summary>
public static class FavoriteFilter
{
    /// <summary>
    /// Sem favoritos, todas as saídas. Com favoritos, as favoritas que estão ativas (ordem alfabética do D7 da Fase 3)
    /// e, se a saída ativa (<paramref name="activeId"/>) não for favorita, ela no fim.
    /// </summary>
    public static List<DeviceInfo> Visible(IReadOnlyList<DeviceInfo> devices, IReadOnlyCollection<string>? favorites, string? activeId)
    {
        var result = new List<DeviceInfo>();
        if (favorites is null || favorites.Count == 0)
        {
            result.AddRange(devices);
            return result;
        }
        foreach (var d in devices)
            if (IsIn(favorites, d.Id)) result.Add(d);
        result.Sort(Compare);
        if (!string.IsNullOrEmpty(activeId) && !IsIn(favorites, activeId))
        {
            foreach (var d in devices)
            {
                if (string.Equals(d.Id, activeId, StringComparison.OrdinalIgnoreCase)) { result.Add(d); break; }
            }
        }
        return result;
    }

    private static bool IsIn(IReadOnlyCollection<string> favorites, string id)
    {
        foreach (var f in favorites)
            if (string.Equals(f, id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int Compare(DeviceInfo a, DeviceInfo b)
    {
        var c = string.Compare(a.ShortName, b.ShortName, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }
}
