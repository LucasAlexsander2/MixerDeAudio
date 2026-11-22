using System;
using System.Collections.Generic;
using MixerDeAudio.Interop;

namespace MixerDeAudio.Pc;

/// <summary>
/// Entrada de verdade pelo <c>SendInput</c> (Fase 9b, D13 a D15). Seguro em qualquer thread; só o <c>RealPc</c> usa, então
/// nunca roda com <c>--teste-pc</c>. Nunca grava o que foi digitado no log (D12).
/// </summary>
internal static class InputSender
{
    private const int WheelDelta = 120;

    public static void MouseMove(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        NativeMethods.SendInputs(new[] { NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_MOVE, dx, dy) });
    }

    public static void MouseClick(MouseButton button, bool doubleClick)
    {
        var (down, up) = Flags(button);
        var one = new[] { NativeMethods.MouseInput(down), NativeMethods.MouseInput(up) };
        if (!doubleClick)
        {
            NativeMethods.SendInputs(one);
            return;
        }
        NativeMethods.SendInputs(new[] { one[0], one[1], one[0], one[1] });
    }

    public static void MouseButtonSet(MouseButton button, bool pressed)
    {
        var (down, up) = Flags(button);
        NativeMethods.SendInputs(new[] { NativeMethods.MouseInput(pressed ? down : up) });
    }

    public static void MouseScroll(int dx, int dy)
    {
        var list = new List<NativeMethods.INPUT>(2);
        if (dy != 0) list.Add(NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)(dy * WheelDelta))));
        if (dx != 0) list.Add(NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_HWHEEL, 0, 0, unchecked((uint)(dx * WheelDelta))));
        NativeMethods.SendInputs(list.ToArray());
    }

    public static void MouseAbsolute(int x, int y) =>
        NativeMethods.SendInputs(new[]
        {
            NativeMethods.MouseInput(NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK, x, y),
        });

    public static void Send(IReadOnlyList<KeyEvent> events)
    {
        if (events.Count == 0) return;
        var inputs = new NativeMethods.INPUT[events.Count];
        for (var i = 0; i < inputs.Length; i++)
        {
            var e = events[i];
            var flags = e.Up ? NativeMethods.KEYEVENTF_KEYUP : 0u;
            if (e.IsUnicode)
            {
                inputs[i] = NativeMethods.KeyInput(0, e.Code, flags | NativeMethods.KEYEVENTF_UNICODE);
            }
            else
            {
                if (e.Extended) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
                var scan = (ushort)NativeMethods.MapVirtualKeyW(e.Code, 0);
                inputs[i] = NativeMethods.KeyInput(e.Code, scan, flags);
            }
        }
        NativeMethods.SendInputs(inputs);
    }

    private static (uint Down, uint Up) Flags(MouseButton button) => button switch
    {
        MouseButton.Right => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
        MouseButton.Middle => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
        _ => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP),
    };
}
