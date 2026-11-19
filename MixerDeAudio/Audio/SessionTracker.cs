using System;
using System.Collections.Generic;
using System.Linq;

namespace MixerDeAudio.Audio;

/// <summary>Foto de um grupo (uma linha por app) num dado momento.</summary>
/// <param name="Key">Chave do grupo (D9).</param>
/// <param name="Name">Nome do app (o da sessão mais antiga do grupo).</param>
/// <param name="ExePath">Caminho do .exe da sessão mais antiga (para o ícone).</param>
/// <param name="Volume">Volume da sessão mais antiga, de 0 a 1 (D10).</param>
/// <param name="Muted">Mudo só se todas as sessões estiverem mudas (D10).</param>
/// <param name="IsSystem">Se é "Sons do sistema".</param>
/// <param name="SessionCount">Quantidade de sessões no grupo.</param>
public sealed record GroupSnapshot(string Key, string Name, string? ExePath, float Volume, bool Muted, bool IsSystem, int SessionCount);

public enum GroupChangeKind
{
    Added,
    Changed,
    Removed,
}

/// <summary>Aviso de mudança. <c>OldIndex</c> é a posição antes (-1 se novo); <c>Index</c> é a posição depois (-1 se removido).</summary>
public sealed record GroupChange(GroupChangeKind Kind, GroupSnapshot Group, int OldIndex, int Index);

/// <summary>
/// Mantém as sessões agrupadas por app (D8 a D12) e avisa o que mudou. Lógica pura, sem NAudio e sem WinUI.
/// Não é thread-safe: usar só na thread da tela.
/// </summary>
public sealed class SessionTracker
{
    public const string SystemKey = "::sons-do-sistema";

    private sealed class Group
    {
        public required string Key;
        public readonly List<SessionInfo> Sessions = new(); // ordem de chegada: a primeira é a mais antiga

        public GroupSnapshot Snapshot()
        {
            var first = Sessions[0];
            return new GroupSnapshot(Key, first.Name, first.ExePath, first.Volume, Sessions.All(s => s.Muted), first.IsSystem, Sessions.Count);
        }
    }

    private readonly List<Group> _groups = new(); // sempre na ordem de exibição (D12)
    private readonly Dictionary<string, string> _keyBySession = new(StringComparer.Ordinal);

    /// <summary>Chamado a cada mudança, já com as posições novas.</summary>
    public event Action<GroupChange>? Changed;

    public IReadOnlyList<GroupSnapshot> Groups => _groups.Select(g => g.Snapshot()).ToList();

    public int SessionCount => _keyBySession.Count;

    /// <summary>Chave do grupo de uma sessão, ou nulo se a sessão não existe.</summary>
    public string? KeyOf(string sessionId) => _keyBySession.TryGetValue(sessionId, out var k) ? k : null;

    /// <summary>Ids das sessões de um grupo, da mais antiga para a mais nova.</summary>
    public IReadOnlyList<string> SessionIdsOf(string key)
    {
        var g = Find(key);
        return g is null ? Array.Empty<string>() : g.Sessions.Select(s => s.Id).ToList();
    }

    /// <summary>Chave do grupo para uma sessão (D9).</summary>
    public static string KeyFor(SessionInfo s)
    {
        if (s.IsSystem) return SystemKey;
        if (!string.IsNullOrWhiteSpace(s.ExePath)) return s.ExePath.ToLowerInvariant();
        return s.Id;
    }

    /// <summary>Compara dois grupos na ordem de exibição (D12): alfabética, "Sons do sistema" por último.</summary>
    private static int Compare(GroupSnapshot a, GroupSnapshot b)
    {
        if (a.IsSystem != b.IsSystem) return a.IsSystem ? 1 : -1;
        return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
    }

    public void Add(SessionInfo info)
    {
        if (info.State == SessionState.Expired) return;
        if (_keyBySession.ContainsKey(info.Id))
        {
            // Id repetido não duplica: vale como atualização.
            UpdateVolume(info.Id, info.Volume, info.Muted);
            UpdateState(info.Id, info.State);
            UpdateName(info.Id, info.Name);
            return;
        }

        var key = KeyFor(info);
        _keyBySession[info.Id] = key;
        var group = Find(key);
        if (group is null)
        {
            group = new Group { Key = key };
            group.Sessions.Add(info);
            var snap = group.Snapshot();
            var index = InsertionIndex(snap);
            _groups.Insert(index, group);
            Changed?.Invoke(new GroupChange(GroupChangeKind.Added, snap, -1, index));
        }
        else
        {
            Mutate(group, () => group.Sessions.Add(info));
        }
    }

    public void UpdateVolume(string sessionId, float volume, bool muted) =>
        Update(sessionId, s => s with { Volume = volume, Muted = muted });

    public void UpdateState(string sessionId, SessionState state)
    {
        if (state == SessionState.Expired) Remove(sessionId);
        else Update(sessionId, s => s with { State = state });
    }

    public void UpdateName(string sessionId, string name) => Update(sessionId, s => s with { Name = name });

    public void Remove(string sessionId)
    {
        if (!_keyBySession.Remove(sessionId, out var key)) return;
        var group = Find(key);
        if (group is null) return;
        var oldIndex = _groups.IndexOf(group);
        var before = group.Snapshot();
        group.Sessions.RemoveAll(s => s.Id == sessionId);
        if (group.Sessions.Count == 0)
        {
            _groups.RemoveAt(oldIndex);
            Changed?.Invoke(new GroupChange(GroupChangeKind.Removed, before, oldIndex, -1));
            return;
        }
        Reposition(group, oldIndex, before);
    }

    private void Update(string sessionId, Func<SessionInfo, SessionInfo> change)
    {
        if (!_keyBySession.TryGetValue(sessionId, out var key)) return;
        var group = Find(key);
        if (group is null) return;
        Mutate(group, () =>
        {
            var i = group.Sessions.FindIndex(s => s.Id == sessionId);
            if (i >= 0) group.Sessions[i] = change(group.Sessions[i]);
        });
    }

    /// <summary>Aplica uma alteração ao grupo e avisa só se a foto mudou.</summary>
    private void Mutate(Group group, Action apply)
    {
        var oldIndex = _groups.IndexOf(group);
        var before = group.Snapshot();
        apply();
        Reposition(group, oldIndex, before);
    }

    private void Reposition(Group group, int oldIndex, GroupSnapshot before)
    {
        var after = group.Snapshot();
        _groups.RemoveAt(oldIndex);
        var sameSortKey = before.Name == after.Name && before.IsSystem == after.IsSystem;
        var newIndex = sameSortKey ? oldIndex : InsertionIndex(after);
        _groups.Insert(newIndex, group);
        if (after != before || newIndex != oldIndex)
            Changed?.Invoke(new GroupChange(GroupChangeKind.Changed, after, oldIndex, newIndex));
    }

    /// <summary>Posição de inserção na ordem de exibição; em empate de nome, depois dos que já estão.</summary>
    private int InsertionIndex(GroupSnapshot snap)
    {
        var i = 0;
        while (i < _groups.Count && Compare(_groups[i].Snapshot(), snap) <= 0) i++;
        return i;
    }

    private Group? Find(string key) => _groups.FirstOrDefault(g => g.Key == key);
}
