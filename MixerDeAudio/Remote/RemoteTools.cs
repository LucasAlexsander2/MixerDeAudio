using System;
using System.Collections.Generic;

namespace MixerDeAudio.Remote;

/// <summary>Grupos de ferramentas do PC que o celular pode usar (Fase 9, D1). Lógica pura.</summary>
public static class RemoteTools
{
    public const string Power = "energia";
    public const string Display = "monitores";
    public const string Input = "entrada";
    public const string Screen = "tela";
    public const string Windows = "janelas";
    public const string Shortcuts = "atalhos";
    public const string Transfer = "transferencia";
    public const string Info = "estado";

    /// <summary>Todos os ids, na ordem mostrada nas Configurações e no estado (<c>ferr</c>).</summary>
    public static readonly IReadOnlyList<string> All = new[] { Power, Display, Input, Screen, Windows, Shortcuts, Transfer, Info };

    public static bool IsKnown(string? id)
    {
        if (id is null) return false;
        foreach (var k in All)
            if (string.Equals(k, id, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>Só ids conhecidos, sem repetir, na ordem de <see cref="All"/>. Nulo vira lista vazia (tudo desligado).</summary>
    public static List<string> Normalize(IEnumerable<string>? ids)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (ids is not null)
            foreach (var id in ids)
                if (IsKnown(id)) set.Add(id);
        var result = new List<string>(set.Count);
        foreach (var k in All)
            if (set.Contains(k)) result.Add(k);
        return result;
    }

    public static bool IsEnabled(IEnumerable<string>? ids, string id)
    {
        if (ids is null) return false;
        foreach (var k in ids)
            if (string.Equals(k, id, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>A lista com o grupo ligado ou desligado (já normalizada).</summary>
    public static List<string> With(IEnumerable<string>? ids, string id, bool on)
    {
        var list = new List<string>(ids ?? Array.Empty<string>());
        list.RemoveAll(k => string.Equals(k, id, StringComparison.Ordinal));
        if (on) list.Add(id);
        return Normalize(list);
    }
}

/// <summary>O que fazer quando um celular tenta entrar (Fase 9, D3).</summary>
public enum SessionDecision
{
    Accept,
    /// <summary>Mesma chave: fecha a sessão antiga e aceita a nova.</summary>
    Replace,
    /// <summary>Outro celular está no controle.</summary>
    Refuse,
}

/// <summary>Um celular por vez (Fase 9, D3). Lógica pura.</summary>
public static class SingleSessionPolicy
{
    /// <param name="currentHash">Hash da chave do celular conectado agora, ou nulo se não há ninguém.</param>
    /// <param name="newHash">Hash da chave de quem está entrando.</param>
    public static SessionDecision Decide(string? currentHash, string newHash)
    {
        if (currentHash is null) return SessionDecision.Accept;
        return string.Equals(currentHash, newHash, StringComparison.Ordinal) ? SessionDecision.Replace : SessionDecision.Refuse;
    }
}

/// <summary>A que grupo de ferramentas um comando pertence (Fase 9, D6). Comandos de áudio e de sessão não têm grupo.</summary>
public static class RemoteCommandGroups
{
    public static string? GroupOf(RemoteCommand command) => command switch
    {
        PowerCommand or PowerCancelCommand => RemoteTools.Power,
        ProjectionCommand or ScreensCommand => RemoteTools.Display,
        InputCommand or AwakeCommand => RemoteTools.Input,
        WindowsCommand or WindowActionCommand => RemoteTools.Windows,
        ShortcutCommand => RemoteTools.Shortcuts,
        ClipReadCommand or ClipWriteCommand => RemoteTools.Transfer,
        SleepCommand => RemoteTools.Info,
        SubscribeCommand sub => sub.Group switch { "janelas" => RemoteTools.Windows, "info" => RemoteTools.Info, _ => null },
        _ => null,
    };
}
