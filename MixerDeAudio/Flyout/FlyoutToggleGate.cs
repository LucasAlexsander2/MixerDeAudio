using System;

namespace MixerDeAudio.Flyout;

/// <summary>
/// Regra de alternância da janelinha (Fase 1, D16). Clicar no ícone com a janela aberta faz a janela perder o foco e fechar
/// antes de o clique chegar: por isso um pedido de abrir logo depois de um fechamento por perda de foco é ignorado.
/// Lógica pura, com relógio injetável.
/// </summary>
public sealed class FlyoutToggleGate
{
    /// <summary>Janela, depois de um fechamento por perda de foco, em que o pedido de abrir é ignorado.</summary>
    public static readonly TimeSpan BlockWindow = TimeSpan.FromMilliseconds(400);

    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset? _focusLostAt;

    public FlyoutToggleGate(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Registra um fechamento. Só o fechamento por perda de foco bloqueia; os outros liberam.</summary>
    public void NotifyClosed(bool lostFocus) => _focusLostAt = lostFocus ? _now() : null;

    /// <summary>Diz se um pedido de abrir feito agora deve ser atendido.</summary>
    public bool CanOpen() => _focusLostAt is not { } at || _now() - at >= BlockWindow;
}
