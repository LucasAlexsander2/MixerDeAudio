using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MixerDeAudio.Pc;

/// <summary>
/// Tudo o que o celular pode fazer no PC (Fase 9, D7). O app usa <c>RealPc</c>; com <c>--teste-pc</c> usa <c>RecordingPc</c>,
/// que só grava no log. Chamado sempre na thread da tela.
/// </summary>
public interface IPcActions
{
    /// <summary>Desligar, reiniciar, suspender ou bloquear, na hora.</summary>
    void Power(PowerAction action);

    /// <summary>Troca a projeção como o Win+P. Devolve falso se o Windows recusou.</summary>
    bool SetProjection(ProjectionMode mode);

    /// <summary>Apaga as telas (o PC continua ligado).</summary>
    void ScreensOff();

    /// <summary>Acorda as telas.</summary>
    void ScreensOn();

    // ---- Brilho (Fase 12, D6 e D11): chamado só no BrightnessWorker, nunca na thread da tela ----

    /// <summary>Brilho (0 a 100) de cada tela, na ordem de <c>ScreenCapture.Monitors()</c>; nulo onde não há DDC/CI.</summary>
    IReadOnlyList<int?> ReadBrightness();

    /// <summary>Muda o brilho da tela (0 a 100). Falso se o monitor recusou.</summary>
    bool SetBrightness(int tela, int pct);

    // ---- Entrada (Fase 9b, D13 a D15): chamada na thread de rede do celular, nunca na da tela ----

    /// <summary>Move o mouse de forma relativa (já cortado em ±1000).</summary>
    void MouseMove(int dx, int dy);

    void MouseClick(MouseButton button, bool doubleClick);

    /// <summary>Aperta ou solta um botão (arrastar).</summary>
    void MouseButtonSet(MouseButton button, bool down);

    /// <summary>Rola em "dentes" (já cortado em ±20): <c>dy</c> positivo rola para cima.</summary>
    void MouseScroll(int dx, int dy);

    /// <summary>Move o mouse para a coordenada absoluta (0 a 65535) da área de trabalho virtual.</summary>
    void MouseAbsolute(int x, int y);

    void TypeText(string text);

    /// <summary>Tecla da lista fixa de <see cref="InputRules"/> (o nome já foi validado).</summary>
    void PressKey(string name);

    /// <summary>"Manter a tela ligada" (D16). Só na thread da tela: o estado de execução é da thread.</summary>
    void KeepAwake(bool on);

    // ---- Janelas, atalhos e timer de sono (Fase 9c), na thread da tela ----

    /// <summary>Traz para a frente, minimiza ou fecha uma janela (já conferida contra a lista mostrada ao celular, D22).</summary>
    void ControlWindow(WindowItem window, WindowAction action);

    /// <summary>Abre o atalho da pasta pelo shell (<paramref name="path"/> montado no PC; <paramref name="name"/> é o nome mostrado).</summary>
    void OpenShortcut(string path, string name);

    /// <summary>Volume geral do timer de sono (0 a 1).</summary>
    void SleepVolume(float volume);

    /// <summary>Pausa os apps de mídia que estão tocando; devolve quantos foram pausados.</summary>
    Task<int> SleepPauseAll();

    // ---- Área de transferência (Fase 9d, D25 e D28), na thread da tela ----

    /// <summary>O texto da área de transferência; vazio se não há texto; nulo se não foi possível lê-la.</summary>
    string? ReadClipboard();

    /// <summary>Grava o texto na área de transferência. Devolve falso se não foi possível.</summary>
    bool WriteClipboard(string text);
}

/// <summary>Grava no log o que seria feito, sem tocar no PC (Fase 9, D7). Só para o modo <c>--teste-pc</c>.</summary>
public sealed class RecordingPc : IPcActions
{
    private readonly Action<string> _sink;

    public RecordingPc(Action<string> sink) => _sink = sink;

    public void Power(PowerAction action) => _sink($"teste-pc: energia {PowerRules.Id(action)}");

    public bool SetProjection(ProjectionMode mode)
    {
        _sink($"teste-pc: projecao {DisplayTopology.Id(mode)}");
        return true;
    }

    public void ScreensOff() => _sink("teste-pc: telas apagar");

    public void ScreensOn() => _sink("teste-pc: telas acordar");

    // Brilho (D11): valores em memória (uma tela, começa em 50); o DDC real nunca é lido nem mudado no modo de teste
    private readonly int?[] _brightness = { 50 };

    public IReadOnlyList<int?> ReadBrightness()
    {
        lock (_brightness) return (int?[])_brightness.Clone();
    }

    public bool SetBrightness(int tela, int pct)
    {
        lock (_brightness)
        {
            if (tela < 0 || tela >= _brightness.Length) return false;
            _brightness[tela] = pct;
        }
        System.Threading.Thread.Sleep(60); // imita a demora do DDC real (40 a 100 ms); só roda no worker, nunca na thread da tela
        _sink($"teste-pc: brilho {tela} {pct}");
        return true;
    }

    // Entrada: só aqui (modo de teste, com textos de teste) o conteúdo vai para o log (D12)
    public void MouseMove(int dx, int dy) => _sink($"teste-pc: entrada mouse {dx} {dy}");

    public void MouseClick(MouseButton button, bool doubleClick) =>
        _sink($"teste-pc: entrada clique {InputRules.ButtonId(button)}{(doubleClick ? " duplo" : "")}");

    public void MouseButtonSet(MouseButton button, bool down) =>
        _sink($"teste-pc: entrada botao {InputRules.ButtonId(button)} {(down ? "baixo" : "cima")}");

    public void MouseScroll(int dx, int dy) => _sink($"teste-pc: entrada rolar {dx} {dy}");

    public void MouseAbsolute(int x, int y) => _sink($"teste-pc: entrada mouseAbs {x} {y}");

    public void TypeText(string text) =>
        _sink($"teste-pc: entrada texto {InputRules.TextEvents(text).Count} eventos: {text.Replace("\r", "\\r").Replace("\n", "\\n")}");

    public void PressKey(string name)
    {
        InputRules.TryKeyOrCombo(name, out var events);
        _sink($"teste-pc: entrada tecla {name.ToLowerInvariant()} {events.Count} eventos");
    }

    public void KeepAwake(bool on) => _sink($"teste-pc: acordado {(on ? "on" : "off")}");

    // Janelas e atalhos: sem o título (D22); o nome do atalho vai (só há atalhos de teste aqui)
    public void ControlWindow(WindowItem window, WindowAction action) => _sink($"teste-pc: janela {WindowFilter.ActionId(action)}");

    public void OpenShortcut(string path, string name) => _sink($"teste-pc: atalho {name}");

    public void SleepVolume(float volume) =>
        _sink($"teste-pc: sono volume {volume.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");

    public Task<int> SleepPauseAll()
    {
        _sink("teste-pc: sono pausar");
        return Task.FromResult(0);
    }

    // Área de transferência (D28): uma variável em memória; a real nunca é lida nem gravada no modo de teste
    private string _clipboard = "";

    public string? ReadClipboard()
    {
        _sink("teste-pc: area de transferencia ler");
        return _clipboard;
    }

    public bool WriteClipboard(string text)
    {
        _clipboard = text;
        _sink("teste-pc: area de transferencia gravar " + text.ReplaceLineEndings(" / "));
        return true;
    }
}

/// <summary>Escolhe as ações do PC (Fase 9, D7): no modo de teste o <c>RealPc</c> nunca é criado.</summary>
public static class PcActionsFactory
{
    public static IPcActions Create(bool testMode, Func<IPcActions> real, Func<IPcActions> recording) =>
        testMode ? recording() : real();
}
