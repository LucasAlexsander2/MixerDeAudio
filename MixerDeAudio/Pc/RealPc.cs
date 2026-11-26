using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MixerDeAudio.Interop;
using MixerDeAudio.Services;

namespace MixerDeAudio.Pc;

/// <summary>Faz de verdade o que o celular pede (Fase 9, D7 a D10). Nunca é criado com <c>--teste-pc</c>.</summary>
internal sealed class RealPc : IPcActions
{
    public void Power(PowerAction action)
    {
        switch (action)
        {
            case PowerAction.Shutdown: RunShutdown("/s"); break;
            case PowerAction.Restart: RunShutdown("/r"); break;
            case PowerAction.Suspend:
                // Sem hibernar e sem forçar: o Windows pede a cada app que salve, como no menu Iniciar
                NativeMethods.SetSuspendState(false, false, false);
                break;
            case PowerAction.Lock:
                NativeMethods.LockWorkStation();
                break;
        }
    }

    /// <summary>Sem <c>/f</c>: com <c>/t 0</c> o Windows não força o fim dos apps.</summary>
    private static void RunShutdown(string flag)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        using var p = Process.Start(new ProcessStartInfo(exe, $"{flag} /t 0") { UseShellExecute = false, CreateNoWindow = true });
    }

    public bool SetProjection(ProjectionMode mode) => DisplayTopology.Apply(mode);

    public void ScreensOff()
    {
        // PostMessage: o SendMessage em broadcast trava até todas as janelas responderem
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MONITORPOWER, 2);
    }

    public void ScreensOn()
    {
        // O SC_MONITORPOWER -1 não acorda de forma confiável no Windows 11: um pingo de mouse (+1 e -1 px) acorda
        NativeMethods.MoveMouseBy(1, 0);
        NativeMethods.MoveMouseBy(-1, 0);
    }

    public IReadOnlyList<int?> ReadBrightness() => Brightness.Read();

    public bool SetBrightness(int tela, int pct) => Brightness.Set(tela, pct);

    public void MouseMove(int dx, int dy) => InputSender.MouseMove(dx, dy);
    public void MouseClick(MouseButton button, bool doubleClick) => InputSender.MouseClick(button, doubleClick);
    public void MouseButtonSet(MouseButton button, bool down) => InputSender.MouseButtonSet(button, down);
    public void MouseScroll(int dx, int dy) => InputSender.MouseScroll(dx, dy);
    public void MouseAbsolute(int x, int y) => InputSender.MouseAbsolute(x, y);
    public void TypeText(string text) => InputSender.Send(InputRules.TextEvents(text));

    public void PressKey(string name)
    {
        if (InputRules.TryKeyOrCombo(name, out var events)) InputSender.Send(events);
    }

    public void KeepAwake(bool on) => MixerDeAudio.Pc.KeepAwake.Set(on);

    public void ControlWindow(WindowItem window, WindowAction action) => WindowList.Perform(new IntPtr(window.Id), action);

    /// <summary>Abre pelo shell (o <c>.lnk</c> ou <c>.url</c> da pasta de atalhos); nunca mostra o nome nem o caminho no log.</summary>
    public void OpenShortcut(string path, string name)
    {
        using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void SleepVolume(float volume) => App.Hub.SetMasterVolume(volume);

    public Task<int> SleepPauseAll() => App.Hub.PauseAllMediaAsync();

    public string? ReadClipboard() => ClipboardBridge.ReadText();

    public bool WriteClipboard(string text) => ClipboardBridge.WriteText(text);
}
