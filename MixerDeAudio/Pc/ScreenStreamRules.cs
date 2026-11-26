using System;
using System.Text.Json;

namespace MixerDeAudio.Pc;

/// <summary>O que a página mandou pelo canal da tela (Fase 10, D1 e D2).</summary>
public enum StreamMessageKind { Invalid, Config, Ok }

/// <summary>
/// Mensagem do canal: <c>{"mon":i,"w":largura,"q":"eco|normal|alta"}</c> (Config) ou <c>{"ok":1}</c> (Ok). Fase 15c, D16: a
/// configuração aceita também <c>"cur"</c> (tamanho do ponteiro: 1, 1.5, 2 ou 3; inválido = 1) e <c>"p":1</c> (mandar a posição
/// do ponteiro antes de cada quadro). Sem eles valem 1 e falso: o canal faz o que sempre fez.
/// </summary>
public readonly record struct StreamMessage(StreamMessageKind Kind, int Monitor, int Width, string Quality, double Cursor = 1.0, bool Pointer = false);

/// <summary>O que fazer com o quadro capturado.</summary>
public enum FrameAction { Send, Skip }

/// <summary>Regras puras do laço da transmissão da tela (Fase 10, D2, D4 e D5).</summary>
public static class ScreenStreamRules
{
    /// <summary>Lê uma mensagem de texto do canal. JSON inválido ou sem campos conhecidos vira <c>Invalid</c>.</summary>
    public static StreamMessage Parse(ReadOnlySpan<byte> utf8)
    {
        var invalid = new StreamMessage(StreamMessageKind.Invalid, 0, 0, "");
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return invalid;
            if (root.TryGetProperty("ok", out _)) return new StreamMessage(StreamMessageKind.Ok, 0, 0, "");
            var hasAny = false;
            int mon = 0, w = 0;
            var q = "";
            if (root.TryGetProperty("mon", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetInt32(out var mv)) { mon = mv; hasAny = true; }
            if (root.TryGetProperty("w", out var wv) && wv.ValueKind == JsonValueKind.Number && wv.TryGetInt32(out var wi)) { w = wi; hasAny = true; }
            if (root.TryGetProperty("q", out var qv) && qv.ValueKind == JsonValueKind.String) { q = qv.GetString() ?? ""; hasAny = true; }
            var cur = 1.0;
            if (root.TryGetProperty("cur", out var cv))
            {
                hasAny = true;
                if (cv.ValueKind == JsonValueKind.Number && cv.TryGetDouble(out var cd)) cur = CursorScale(cd);
            }
            var pointer = false;
            if (root.TryGetProperty("p", out var pv))
            {
                hasAny = true;
                pointer = pv.ValueKind == JsonValueKind.True || (pv.ValueKind == JsonValueKind.Number && pv.TryGetDouble(out var pd) && pd == 1);
            }
            return hasAny ? new StreamMessage(StreamMessageKind.Config, Math.Max(0, mon), Math.Max(0, w), q, cur, pointer) : invalid;
        }
        catch (JsonException)
        {
            return invalid;
        }
    }

    /// <summary>Tamanho do ponteiro pedido (D16): só 1, 1,5, 2 e 3 valem; qualquer outro valor é 1.</summary>
    public static double CursorScale(double value) => value switch
    {
        1.5 => 1.5,
        2.0 => 2.0,
        3.0 => 3.0,
        _ => 1.0,
    };

    /// <summary>
    /// Posição do ponteiro dentro do quadro, de 0 a 1 em cada eixo: <c>(x - left) / width</c>, cortada nas bordas (ponteiro fora
    /// da tela capturada fica na borda mais próxima). Largura ou altura zero dá o centro.
    /// </summary>
    public static (double X, double Y) PointerFraction(int x, int y, int left, int top, int width, int height) =>
        (width <= 0 ? 0.5 : Math.Clamp((double)(x - left) / width, 0.0, 1.0),
         height <= 0 ? 0.5 : Math.Clamp((double)(y - top) / height, 0.0, 1.0));

    /// <summary>O texto mandado antes de cada quadro com <c>p:1</c>: <c>{"p":[x,y]}</c>, número com ponto e até 4 casas.</summary>
    public static string PointerMessage(double x, double y)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "{\"p\":[" + Math.Clamp(x, 0.0, 1.0).ToString("0.####", inv) + "," + Math.Clamp(y, 0.0, 1.0).ToString("0.####", inv) + "]}";
    }

    /// <summary>Pode capturar: só com o "ok" do quadro anterior recebido e o intervalo da qualidade vencido.</summary>
    public static bool CanCapture(bool okReceived, TimeSpan sinceLastCapture, TimeSpan interval) =>
        okReceived && sinceLastCapture >= interval;

    /// <summary>Quanto falta para o intervalo vencer (zero se já venceu).</summary>
    public static TimeSpan RemainingDelay(TimeSpan sinceLastCapture, TimeSpan interval) =>
        sinceLastCapture >= interval ? TimeSpan.Zero : interval - sinceLastCapture;

    /// <summary>Quadro igual ao anterior (mesmo tamanho e mesmos bytes), comparação vetorizada.</summary>
    public static bool SameFrame(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current) =>
        previous.Length == current.Length && previous.SequenceEqual(current);

    /// <summary>Igual ao anterior: não vai. Primeiro quadro, tela ou largura nova: sempre vai.</summary>
    public static FrameAction Decide(bool buffersRebuilt, bool identical) =>
        !buffersRebuilt && identical ? FrameAction.Skip : FrameAction.Send;

    /// <summary>Mudou a tela ou o tamanho do quadro: os buffers são refeitos (e o quadro anterior deixa de valer).</summary>
    public static bool NeedsRebuild(int curMonitor, int curWidth, int curHeight, int newMonitor, int newWidth, int newHeight) =>
        curMonitor != newMonitor || curWidth != newWidth || curHeight != newHeight || curWidth <= 0 || curHeight <= 0;
}
