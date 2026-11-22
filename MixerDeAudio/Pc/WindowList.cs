using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MixerDeAudio.Interop;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Pc;

/// <summary>
/// Janelas abertas (Fase 9c, D21 e D22): <c>EnumWindows</c> + filtro puro (<see cref="WindowFilter"/>). Só leitura; as ações
/// (<see cref="Perform"/>) só rodam em janelas que estavam na última lista mostrada ao celular. Criado no primeiro pedido; não guarda
/// nada além da última lista e do mapa de ícones (que tem no máximo <see cref="WindowFilter.MaxItems"/> itens). Thread da tela.
/// </summary>
internal sealed class WindowList
{
    private static readonly object IconLock = new();
    private static Dictionary<string, string> _icons = new(StringComparer.Ordinal);

    private IReadOnlyList<WindowItem> _shown = Array.Empty<WindowItem>();

    /// <summary>A lista para o celular (e passa a ser a "última lista mostrada" das ações). Nada de janela do próprio app.</summary>
    public IReadOnlyList<WindowItem> List(bool testMode)
    {
        var list = Enumerate(testMode);
        _shown = list;
        var icons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var w in list)
            if (!string.IsNullOrEmpty(w.ExePath)) icons[RemoteProtocol.WindowIconId(w.ExePath)] = w.ExePath;
        lock (IconLock) _icons = icons;
        return list;
    }

    /// <summary>O alvo de uma ação (D22): relê a lista na hora e confere o id e o processo com o que o celular viu. Nulo = ignorar.</summary>
    public WindowItem? Resolve(long id, bool testMode) => WindowFilter.Resolve(_shown, Enumerate(testMode), id);

    /// <summary>Esquece a lista e os ícones (grupo desligado).</summary>
    public void Clear()
    {
        _shown = Array.Empty<WindowItem>();
        lock (IconLock) _icons = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>O <c>.exe</c> do ícone de uma janela da última lista (rota <c>/icone</c>), ou nulo.</summary>
    public static string? IconPathFor(string iconId)
    {
        lock (IconLock) return _icons.TryGetValue(iconId, out var path) ? path : null;
    }

    // ---- Leitura ----

    private sealed class Context
    {
        public readonly List<WindowItem> Items = new();
        public readonly Dictionary<uint, string?> ExeByPid = new();
        public readonly char[] Buffer = new char[512];
        public bool TestMode;
        public int OwnPid;
    }

    private static unsafe List<WindowItem> Enumerate(bool testMode)
    {
        var ctx = new Context { TestMode = testMode, OwnPid = Environment.ProcessId };
        var handle = GCHandle.Alloc(ctx);
        try
        {
            NativeMethods.EnumWindows(&EnumProc, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return ctx.Items;
    }

    [UnmanagedCallersOnly]
    private static int EnumProc(IntPtr hwnd, IntPtr data)
    {
        try
        {
            var ctx = (Context)GCHandle.FromIntPtr(data).Target!;
            if (ctx.Items.Count >= WindowFilter.MaxItems) return 0;
            // Do mais barato para o mais caro: só o que passa nos primeiros testes pede título e processo
            if (!NativeMethods.IsWindowVisible(hwnd)) return 1;
            if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return 1;
            var n = NativeMethods.GetWindowText(hwnd, ctx.Buffer, ctx.Buffer.Length);
            if (n <= 0) return 1;
            var title = new string(ctx.Buffer, 0, n);
            var tool = (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0;
            var cloaked = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out var cloakValue, sizeof(int)) == 0 && cloakValue != 0;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            string? exe = null;
            if (pid != 0 && !ctx.ExeByPid.TryGetValue(pid, out exe))
            {
                exe = ExePathOf(pid);
                ctx.ExeByPid[pid] = exe;
            }
            var snap = new WindowSnapshot(hwnd.ToInt64(), title, (int)pid, WindowFilter.ProcessName(exe), exe,
                Visible: true, HasOwner: false, tool, cloaked, NativeMethods.IsIconic(hwnd));
            if (WindowFilter.Include(snap, ctx.OwnPid, ctx.TestMode)) ctx.Items.Add(WindowFilter.ToItem(snap));
        }
        catch
        {
            // um callback nativo não pode lançar: essa janela fica de fora
        }
        return 1;
    }

    private static string? ExePathOf(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return NativeMethods.QueryFullProcessImageNameW(process, 0, buffer, ref size) && size > 0 ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    // ---- Ações (D22) ----

    /// <summary>
    /// Executa a ação numa janela que já foi conferida por <see cref="Resolve"/>. Minimizar e fechar mandam uma mensagem à janela
    /// (<c>WM_SYSCOMMAND/SC_MINIMIZE</c> e <c>WM_CLOSE</c>), sem esperar resposta, então um app travado não trava o Mixer. Nunca mata processo.
    /// </summary>
    public static void Perform(IntPtr hwnd, WindowAction action)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return;
        switch (action)
        {
            case WindowAction.Minimize:
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZE, 0);
                break;
            case WindowAction.Close:
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, 0, 0);
                break;
            default:
                BringToFront(hwnd);
                break;
        }
    }

    /// <summary>Restaura se estiver minimizada e traz para a frente; se o Windows barrar, um toque de Alt e nova tentativa.</summary>
    private static void BringToFront(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        if (NativeMethods.SetForegroundWindow(hwnd)) return;
        NativeMethods.TapAlt();
        Thread.Sleep(30);
        NativeMethods.SetForegroundWindow(hwnd);
    }
}
