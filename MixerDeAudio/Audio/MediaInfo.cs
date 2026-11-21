using System;

namespace MixerDeAudio.Audio;

/// <summary>Estado de reprodução de uma mídia (só o que a janelinha usa).</summary>
public enum MediaStatus
{
    /// <summary>Parada, abrindo ou trocando de faixa.</summary>
    Other,
    Playing,
    Paused,
}

/// <summary>Botões de mídia.</summary>
public enum MediaAction
{
    Previous,
    PlayPause,
    Next,
}

/// <summary>
/// Estado de mídia de um app, vindo do controle de mídia do Windows (Fase 7, D2). Lógica pura.
/// </summary>
/// <param name="Aumid">Identificador do app no controle de mídia (não é o nome do processo).</param>
/// <param name="Title">Título da faixa, sem espaços nas pontas (pode ser vazio; ver <see cref="Text"/>).</param>
/// <param name="Artist">Artista, sem espaços nas pontas (pode ser vazio).</param>
/// <param name="Status">Estado de reprodução.</param>
/// <param name="CanPrev">O app oferece "anterior".</param>
/// <param name="CanNext">O app oferece "próxima".</param>
/// <param name="CanPlayPause">O app oferece play/pausa para o estado atual.</param>
/// <param name="Key">Chave da linha de som ligada, ou nulo quando é só de mídia.</param>
public sealed record MediaInfo(
    string Aumid,
    string Title,
    string Artist,
    MediaStatus Status,
    bool CanPrev,
    bool CanNext,
    bool CanPlayPause,
    string? Key = null)
{
    public bool IsPlaying => Status == MediaStatus.Playing;

    /// <summary>"Título · Artista"; sem artista só o título; título vazio vira o nome do app.</summary>
    public string Text(string appName)
    {
        var title = string.IsNullOrWhiteSpace(Title) ? appName : Title;
        return string.IsNullOrWhiteSpace(Artist) ? title : $"{title} · {Artist}";
    }

    /// <summary>
    /// Play/pausa disponível: o botão alternar, ou "play" quando não está tocando, ou "pausa" quando está tocando.
    /// </summary>
    public static bool PlayPauseAvailable(bool toggle, bool play, bool pause, MediaStatus status) =>
        toggle || (status == MediaStatus.Playing ? pause : play);

    /// <summary>Texto sem espaços nas pontas; nulo vira vazio.</summary>
    public static string Clean(string? text) => text?.Trim() ?? string.Empty;
}
