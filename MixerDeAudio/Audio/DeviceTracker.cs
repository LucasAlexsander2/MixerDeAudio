using System;
using System.Collections.Generic;

namespace MixerDeAudio.Audio;

public enum DeviceChangeKind
{
    Added,
    Changed,
    Removed,
    DefaultChanged,
}

/// <summary>Aviso do <see cref="DeviceTracker"/>. Em <c>DefaultChanged</c>, <c>Device</c> é nulo.</summary>
public sealed record DeviceChange(DeviceChangeKind Kind, DeviceInfo? Device, int Index, int OldIndex);

/// <summary>
/// Lista de saídas ativas, ordenada pelo nome curto (sem diferenciar maiúsculas, id como desempate; D7), e o id da
/// saída padrão (D5). Só uma saída fica ativa: a padrão, se ela estiver na lista. Lógica pura, sem COM.
/// </summary>
public sealed class DeviceTracker
{
    private readonly List<DeviceInfo> _devices = new();
    private string? _defaultId;
    private string? _activeId;

    public event Action<DeviceChange>? Changed;

    public IReadOnlyList<DeviceInfo> Devices => _devices;

    /// <summary>Id da saída padrão do Windows (pode apontar para uma saída que ainda não entrou na lista).</summary>
    public string? DefaultId => _defaultId;

    /// <summary>Id da saída ativa: a padrão, quando ela está na lista; senão nulo.</summary>
    public string? ActiveId => _activeId;

    public bool IsActive(string id) => _activeId is not null && string.Equals(_activeId, id, StringComparison.Ordinal);

    /// <summary>Entra uma saída nova ou atualiza uma que já existe (id repetido não duplica).</summary>
    public void Upsert(DeviceInfo device)
    {
        var old = IndexOf(device.Id);
        if (old >= 0)
        {
            if (_devices[old] == device) return;
            _devices.RemoveAt(old);
            var index = InsertSorted(device);
            Raise(new DeviceChange(DeviceChangeKind.Changed, device, index, old));
            return;
        }
        var at = InsertSorted(device);
        Raise(new DeviceChange(DeviceChangeKind.Added, device, at, -1));
        RefreshActive();
    }

    /// <summary>Tira uma saída. Se era a padrão, nada fica ativo.</summary>
    public void Remove(string id)
    {
        var old = IndexOf(id);
        if (old < 0) return;
        var device = _devices[old];
        _devices.RemoveAt(old);
        Raise(new DeviceChange(DeviceChangeKind.Removed, device, -1, old));
        RefreshActive();
    }

    /// <summary>Define a saída padrão (nulo: nenhuma saída ligada).</summary>
    public void SetDefault(string? id)
    {
        if (string.Equals(_defaultId, id, StringComparison.Ordinal)) return;
        _defaultId = id;
        RefreshActive();
    }

    /// <summary>
    /// Compara a lista lida do Windows com a atual, pelo id: tira o que sumiu, atualiza o que mudou e entra o que é novo.
    /// </summary>
    public void Sync(IReadOnlyCollection<DeviceInfo> current)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in current) ids.Add(d.Id);
        foreach (var old in _devices.ToArray())
            if (!ids.Contains(old.Id)) Remove(old.Id);
        foreach (var d in current) Upsert(d);
    }

    private void RefreshActive()
    {
        var active = _defaultId is not null && IndexOf(_defaultId) >= 0 ? _defaultId : null;
        if (string.Equals(active, _activeId, StringComparison.Ordinal)) return;
        _activeId = active;
        Raise(new DeviceChange(DeviceChangeKind.DefaultChanged, null, -1, -1));
    }

    private int IndexOf(string id) => _devices.FindIndex(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    private int InsertSorted(DeviceInfo device)
    {
        var i = 0;
        while (i < _devices.Count && Compare(_devices[i], device) <= 0) i++;
        _devices.Insert(i, device);
        return i;
    }

    private static int Compare(DeviceInfo a, DeviceInfo b)
    {
        var c = string.Compare(a.ShortName, b.ShortName, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }

    private void Raise(DeviceChange change) => Changed?.Invoke(change);
}
