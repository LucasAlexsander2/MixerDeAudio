using System;
using System.Collections.Generic;

namespace MixerDeAudio.Pc;

/// <summary>Ação sobre uma janela (Fase 9c, D22). Nunca existe "matar processo".</summary>
public enum WindowAction
{
    /// <summary>Trazer para a frente (restaura se estiver minimizada).</summary>
    Front,
    Minimize,
    /// <summary><c>WM_CLOSE</c>: o app pode perguntar se salva.</summary>
    Close,
}

/// <summary>O que o Windows diz de uma janela de nível superior, já lido (Fase 9c, D21). Entrada do filtro puro.</summary>
public sealed record WindowSnapshot(long Id, string Title, int ProcessId, string Process, string? ExePath,
    bool Visible, bool HasOwner, bool ToolWindow, bool Cloaked, bool Minimized);

/// <summary>Uma janela da lista do celular (Fase 9c, D21): id (o HWND), título, nome do processo, <c>.exe</c> (para o ícone) e se está minimizada.</summary>
public sealed record WindowItem(long Id, string Title, string Process, string? ExePath, bool Minimized);

/// <summary>Regras puras da lista de janelas (Fase 9c, D21 e D22).</summary>
public static class WindowFilter
{
    public const int MaxTitle = 120;
    public const int MaxItems = 60;

    /// <summary>No <c>--teste-pc</c>, janelas com este começo de título entram mesmo sendo de ferramenta (D21, D30).</summary>
    public const string TestPrefix = "MX-teste";

    public static bool IsTestTitle(string? title) =>
        title is not null && title.StartsWith(TestPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Como no Alt+Tab: visível, com título, sem dono, sem <c>WS_EX_TOOLWINDOW</c>, não "cloaked" e fora do próprio app.
    /// Exceção de teste: com <paramref name="testMode"/>, a janela de ferramenta cujo título começa com <see cref="TestPrefix"/> entra.
    /// </summary>
    public static bool Include(WindowSnapshot w, int ownProcessId, bool testMode)
    {
        if (!w.Visible || w.HasOwner || w.Cloaked) return false;
        if (string.IsNullOrWhiteSpace(w.Title)) return false;
        if (w.ProcessId == ownProcessId) return false;
        if (w.ToolWindow && !(testMode && IsTestTitle(w.Title))) return false;
        return true;
    }

    /// <summary>Título cortado em <see cref="MaxTitle"/> caracteres (sem partir um par substituto).</summary>
    public static string ClipTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        if (title.Length <= MaxTitle) return title;
        var n = MaxTitle;
        if (char.IsHighSurrogate(title[n - 1])) n--;
        return title.Substring(0, n);
    }

    public static WindowItem ToItem(WindowSnapshot w) =>
        new(w.Id, ClipTitle(w.Title), w.Process, w.ExePath, w.Minimized);

    /// <summary>
    /// O alvo de uma ação (D22): o id tem que estar na lista relida na hora (<paramref name="fresh"/>) <b>e</b> na última lista mostrada ao
    /// celular (<paramref name="shown"/>), com o mesmo processo (o Windows reaproveita HWNDs). Senão devolve nulo e a ação é ignorada.
    /// </summary>
    public static WindowItem? Resolve(IReadOnlyList<WindowItem> shown, IReadOnlyList<WindowItem> fresh, long id)
    {
        WindowItem? before = null;
        foreach (var w in shown)
            if (w.Id == id) { before = w; break; }
        if (before is null) return null;
        foreach (var w in fresh)
            if (w.Id == id)
                return string.Equals(w.Process, before.Process, StringComparison.OrdinalIgnoreCase) ? w : null;
        return null;
    }

    /// <summary>Nome do processo a partir do caminho do <c>.exe</c> (sem extensão); vazio se não houver.</summary>
    public static string ProcessName(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return "";
        var name = exePath.Replace('/', '\\');
        var i = name.LastIndexOf('\\');
        if (i >= 0) name = name.Substring(i + 1);
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    public static string ActionId(WindowAction action) => action switch
    {
        WindowAction.Front => "frente",
        WindowAction.Minimize => "minimizar",
        _ => "fechar",
    };

    public static bool TryParseAction(string? id, out WindowAction action)
    {
        switch (id)
        {
            case "frente": action = WindowAction.Front; return true;
            case "minimizar": action = WindowAction.Minimize; return true;
            case "fechar": action = WindowAction.Close; return true;
            default: action = WindowAction.Front; return false;
        }
    }
}
