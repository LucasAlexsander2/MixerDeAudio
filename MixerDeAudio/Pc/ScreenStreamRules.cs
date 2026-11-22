using System;
using System.Text.Json;

namespace MixerDeAudio.Pc;

/// <summary>O que a página mandou pelo canal da tela (Fase 10, D1 e D2).</summary>
public enum StreamMessageKind { Invalid, Config, Ok }

/// <summary>Mensagem do canal: <c>{"mon":i,"w":largura,"q":"eco|normal|alta"}</c> (Config) ou <c>{"ok":1}</c> (Ok).</summary>
public readonly record struct StreamMessage(StreamMessageKind Kind, int Monitor, int Width, string Quality);

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
            return hasAny ? new StreamMessage(StreamMessageKind.Config, Math.Max(0, mon), Math.Max(0, w), q) : invalid;
        }
        catch (JsonException)
        {
            return invalid;
        }
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
