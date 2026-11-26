using System;
using System.Collections.Generic;

namespace MixerDeAudio.Pc;

/// <summary>Botão do mouse (Fase 9, D14).</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// Um evento de teclado já traduzido (Fase 9, D15). <paramref name="IsUnicode"/>: caractere por <c>KEYEVENTF_UNICODE</c>
/// (<paramref name="Code"/> é a unidade UTF-16); senão <paramref name="Code"/> é o código de tecla virtual.
/// </summary>
public readonly record struct KeyEvent(bool IsUnicode, ushort Code, bool Up, bool Extended = false);

/// <summary>Regras puras de mouse e teclado do celular (Fase 9b, D14 e D15): faixas, lista fixa de teclas e texto.</summary>
public static class InputRules
{
    public const int MaxMouseDelta = 1000;
    public const int MaxScrollNotches = 20;
    public const int MaxTextChars = 1000;

    private const ushort VkShift = 0x10, VkCtrl = 0x11, VkAlt = 0x12, VkLWin = 0x5B, VkEnter = 0x0D;

    private readonly record struct KeyDef(ushort[] Mods, ushort Key, bool Extended);

    private static readonly Dictionary<string, KeyDef> Keys = BuildKeys();

    private static Dictionary<string, KeyDef> BuildKeys()
    {
        var k = new Dictionary<string, KeyDef>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = Plain(VkEnter),
            ["backspace"] = Plain(0x08),
            ["tab"] = Plain(0x09),
            ["esc"] = Plain(0x1B),
            ["space"] = Plain(0x20),
            ["delete"] = Plain(0x2E, true),
            ["home"] = Plain(0x24, true),
            ["end"] = Plain(0x23, true),
            ["pgup"] = Plain(0x21, true),
            ["pgdn"] = Plain(0x22, true),
            ["left"] = Plain(0x25, true),
            ["up"] = Plain(0x26, true),
            ["right"] = Plain(0x27, true),
            ["down"] = Plain(0x28, true),
            ["win"] = Plain(VkLWin, true),
            ["alt+tab"] = new KeyDef(new[] { VkAlt }, 0x09, false),
            ["alt+f4"] = new KeyDef(new[] { VkAlt }, 0x73, false),
            ["win+d"] = new KeyDef(new[] { VkLWin }, 0x44, false),
            ["ctrl+shift+esc"] = new KeyDef(new[] { VkCtrl, VkShift }, 0x1B, false),
            ["shift+f5"] = new KeyDef(new[] { VkShift }, 0x74, false),
            ["b"] = Plain(0x42),
            ["f"] = Plain(0x46),
            ["m"] = Plain(0x4D),
            // mídia
            ["play"] = Plain(0xB3),
            ["next"] = Plain(0xB0),
            ["prev"] = Plain(0xB1),
            ["volup"] = Plain(0xAF),
            ["voldown"] = Plain(0xAE),
            ["mute"] = Plain(0xAD),
        };
        for (var i = 1; i <= 12; i++) k["f" + i] = Plain((ushort)(0x6F + i));
        foreach (var c in "cvxzasfwt") k["ctrl+" + c] = new KeyDef(new[] { VkCtrl }, char.ToUpperInvariant(c), false);
        return k;

        static KeyDef Plain(ushort vk, bool extended = false) => new(Array.Empty<ushort>(), vk, extended);
    }

    /// <summary>Os nomes aceitos (lista fixa).</summary>
    public static IReadOnlyCollection<string> KeyNames => Keys.Keys;

    public static bool IsKnownKey(string? name) => name is not null && Keys.ContainsKey(name);

    /// <summary>
    /// Traduz o nome da tecla na sequência de eventos: modificadores apertados na ordem, a tecla apertada e solta, e os
    /// modificadores soltos na ordem inversa. Falso (e lista vazia) se o nome não está na lista fixa.
    /// </summary>
    public static bool TryKey(string? name, out IReadOnlyList<KeyEvent> events)
    {
        events = Array.Empty<KeyEvent>();
        if (name is null || !Keys.TryGetValue(name, out var def)) return false;
        var list = new List<KeyEvent>(def.Mods.Length * 2 + 2);
        foreach (var m in def.Mods) list.Add(new KeyEvent(false, m, false, m == VkLWin));
        list.Add(new KeyEvent(false, def.Key, false, def.Extended));
        list.Add(new KeyEvent(false, def.Key, true, def.Extended));
        for (var i = def.Mods.Length - 1; i >= 0; i--) list.Add(new KeyEvent(false, def.Mods[i], true, def.Mods[i] == VkLWin));
        events = list;
        return true;
    }

    // ---- Teclas com modificadores presos (Fase 15c, D15) ----

    private static readonly string[] ModNames = { "ctrl", "alt", "shift", "win" };
    private static readonly ushort[] ModCodes = { VkCtrl, VkAlt, VkShift, VkLWin };
    private const string ComboLetters = "cvxzasfwtd";

    /// <summary>Número máximo de modificadores aceitos na lista (os quatro, com repetição tolerada).</summary>
    public const int MaxMods = 8;

    public static bool IsModName(string? name) => name is not null && Array.IndexOf(ModNames, name.ToLowerInvariant()) >= 0;

    /// <summary>
    /// Tecla da lista fixa apertada junto de modificadores (<c>ctrl</c>, <c>alt</c>, <c>shift</c>, <c>win</c>), como o celular manda em
    /// <c>{"t":"tecla","k":"left","mods":["ctrl"]}</c>. A tecla tem de ser uma da lista sem modificador próprio ou uma das letras
    /// dos atalhos da lista (c, v, x, z, a, s, f, w, t, d); com lista de modificadores vazia vale só a tecla da lista. Modificador
    /// desconhecido, tecla fora da lista, lista grande demais ou Ctrl+Alt+Delete recusam. <paramref name="name"/> é o nome
    /// canônico (<c>ctrl+alt+left</c>), que <see cref="TryKeyOrCombo"/> entende de volta.
    /// </summary>
    public static bool TryCombo(string? key, IReadOnlyList<string>? mods, out string name, out IReadOnlyList<KeyEvent> events)
    {
        name = "";
        events = Array.Empty<KeyEvent>();
        if (key is null) return false;
        if (mods is null || mods.Count == 0)
        {
            if (!TryKey(key, out events)) return false;
            name = key.ToLowerInvariant();
            return true;
        }
        if (mods.Count > MaxMods) return false;
        var chosen = new bool[ModNames.Length];
        foreach (var m in mods)
        {
            var i = m is null ? -1 : Array.IndexOf(ModNames, m.ToLowerInvariant());
            if (i < 0) return false;
            chosen[i] = true;
        }
        var k = key.ToLowerInvariant();
        ushort vk;
        var extended = false;
        if (k.Length == 1 && ComboLetters.Contains(k[0])) vk = char.ToUpperInvariant(k[0]);
        else if (Keys.TryGetValue(k, out var def) && def.Mods.Length == 0) { vk = def.Key; extended = def.Extended; }
        else return false;
        if (chosen[0] && chosen[1] && k == "delete") return false; // nunca Ctrl+Alt+Delete
        var list = new List<KeyEvent>();
        var label = new List<string>();
        for (var i = 0; i < ModNames.Length; i++)
        {
            if (!chosen[i]) continue;
            list.Add(new KeyEvent(false, ModCodes[i], false, ModCodes[i] == VkLWin));
            label.Add(ModNames[i]);
        }
        var pressed = list.Count;
        list.Add(new KeyEvent(false, vk, false, extended));
        list.Add(new KeyEvent(false, vk, true, extended));
        for (var i = pressed - 1; i >= 0; i--) list.Add(new KeyEvent(false, list[i].Code, true, list[i].Extended));
        label.Add(k);
        name = string.Join('+', label);
        events = list;
        return true;
    }

    /// <summary>Nome da lista fixa (<see cref="TryKey"/>) ou nome canônico de <see cref="TryCombo"/> (<c>ctrl+left</c>).</summary>
    public static bool TryKeyOrCombo(string? name, out IReadOnlyList<KeyEvent> events)
    {
        if (TryKey(name, out events)) return true;
        events = Array.Empty<KeyEvent>();
        if (name is null) return false;
        var parts = name.Split('+');
        if (parts.Length < 2) return false;
        return TryCombo(parts[^1], parts[..^1], out _, out events);
    }

    /// <summary>
    /// Texto digitado: cada unidade UTF-16 vira um evento Unicode apertado e solto (um emoji são duas); quebra de linha
    /// (<c>\n</c>, <c>\r</c> ou <c>\r\n</c>) vira Enter; outros caracteres de controle (menos Tab) são ignorados.
    /// </summary>
    public static IReadOnlyList<KeyEvent> TextEvents(string? text)
    {
        var list = new List<KeyEvent>();
        if (string.IsNullOrEmpty(text)) return list;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                list.Add(new KeyEvent(false, VkEnter, false));
                list.Add(new KeyEvent(false, VkEnter, true));
            }
            else if (c < 0x20 && c != '\t')
            {
                continue;
            }
            else
            {
                list.Add(new KeyEvent(true, c, false));
                list.Add(new KeyEvent(true, c, true));
            }
        }
        return list;
    }

    /// <summary>Corta o texto em <see cref="MaxTextChars"/> sem partir um par substituto.</summary>
    public static string LimitText(string text)
    {
        if (text.Length <= MaxTextChars) return text;
        var n = MaxTextChars;
        if (char.IsHighSurrogate(text[n - 1])) n--;
        return text.Substring(0, n);
    }

    /// <summary>Movimento do mouse cortado em ±1000 e arredondado.</summary>
    public static int ClampDelta(double value) => ClampRound(value, MaxMouseDelta);

    /// <summary>Rolagem em "dentes" cortada em ±20 e arredondada.</summary>
    public static int ClampScroll(double value) => ClampRound(value, MaxScrollNotches);

    private static int ClampRound(double value, int limit)
    {
        if (double.IsNaN(value)) return 0;
        return (int)Math.Round(Math.Clamp(value, -limit, limit), MidpointRounding.AwayFromZero);
    }

    public static bool TryParseButton(string? id, out MouseButton button)
    {
        switch (id)
        {
            case "esq": button = MouseButton.Left; return true;
            case "dir": button = MouseButton.Right; return true;
            case "meio": button = MouseButton.Middle; return true;
            default: button = MouseButton.Left; return false;
        }
    }

    public static string ButtonId(MouseButton button) => button switch
    {
        MouseButton.Right => "dir",
        MouseButton.Middle => "meio",
        _ => "esq",
    };
}
