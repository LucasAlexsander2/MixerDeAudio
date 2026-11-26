using System;

namespace MixerDeAudio.Pc;

/// <summary>Ações de energia do PC (Fase 9, D8).</summary>
public enum PowerAction
{
    Shutdown,
    Restart,
    Suspend,
    Lock,
}

public static class PowerRules
{
    /// <summary>Menor tempo aceito de um timer (a página oferece 1 min ou mais; 5 s é para o roteiro).</summary>
    public const int MinSeconds = 5;
    public const int MaxSeconds = 86400;
    /// <summary>O aviso "1 min antes" só existe em timers de mais de 1 min.</summary>
    public const int WarnSeconds = 60;

    public static bool TryParse(string? id, out PowerAction action)
    {
        switch (id)
        {
            case "desligar": action = PowerAction.Shutdown; return true;
            case "reiniciar": action = PowerAction.Restart; return true;
            case "suspender": action = PowerAction.Suspend; return true;
            case "bloquear": action = PowerAction.Lock; return true;
            default: action = default; return false;
        }
    }

    public static string Id(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "desligar",
        PowerAction.Restart => "reiniciar",
        PowerAction.Suspend => "suspender",
        _ => "bloquear",
    };

    /// <summary>Espera do "agora" (Fase 16, D13): <c>seg: 0</c> vira 10 s, com a janelinha e o botão Cancelar no PC.</summary>
    public const int NowSeconds = 10;

    /// <summary><c>seg: 0</c> (agora) vira <see cref="NowSeconds"/>; os outros valores ficam como estão.</summary>
    public static int EffectiveSeconds(int seconds) => seconds == 0 ? NowSeconds : seconds;

    /// <summary>Texto da janelinha do "agora": "Desligando em 7 s".</summary>
    public static string NowPromptText(PowerAction action, int secondsLeft)
    {
        var verbo = action switch
        {
            PowerAction.Shutdown => "Desligando",
            PowerAction.Restart => "Reiniciando",
            PowerAction.Suspend => "Suspendendo",
            _ => "Bloqueando",
        };
        return $"{verbo} em {secondsLeft} s";
    }

    /// <summary>0 (agora) ou 5 a 86400 segundos.</summary>
    public static bool IsValidSeconds(int seconds) => seconds == 0 || (seconds >= MinSeconds && seconds <= MaxSeconds);

    /// <summary>"Cancelar ..." do menu da bandeja.</summary>
    public static string CancelText(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Cancelar desligamento programado",
        PowerAction.Restart => "Cancelar reinício programado",
        PowerAction.Suspend => "Cancelar suspensão programada",
        _ => "Cancelar bloqueio programado",
    };

    /// <summary>Texto do balão da bandeja ("O PC vai desligar em 5 min.").</summary>
    public static string BalloonText(PowerAction action, int seconds)
    {
        var verbo = action switch
        {
            PowerAction.Shutdown => "vai desligar",
            PowerAction.Restart => "vai reiniciar",
            PowerAction.Suspend => "vai suspender",
            _ => "vai bloquear",
        };
        return $"O PC {verbo} em {Duration(seconds)}.";
    }

    /// <summary>"30 s", "1 min", "2 h 5 min".</summary>
    public static string Duration(int seconds)
    {
        if (seconds < 60) return $"{seconds} s";
        var min = (seconds + 30) / 60;
        if (min < 60) return $"{min} min";
        var h = min / 60;
        var m = min % 60;
        return m == 0 ? $"{h} h" : $"{h} h {m} min";
    }
}

public enum PowerEventKind
{
    None,
    /// <summary>Falta 1 min para a ação.</summary>
    Warn,
    /// <summary>Chegou a hora: o plano foi consumido.</summary>
    Execute,
}

public readonly record struct PowerEvent(PowerEventKind Kind, PowerAction Action);

/// <summary>Um desligamento (ou reinício etc.) programado.</summary>
public sealed record PowerPlan(PowerAction Action, DateTimeOffset DueAt, DateTimeOffset? WarnAt);

/// <summary>
/// Máquina de estados do timer de energia (Fase 9, D8): um plano por vez, relógio injetável, sem timer próprio.
/// Quem usa dorme até <see cref="DelayToNext"/> e chama <see cref="Poll"/>.
/// </summary>
public sealed class PowerTimer
{
    private readonly Func<DateTimeOffset> _clock;
    private PowerPlan? _plan;
    private bool _warned;

    public PowerTimer(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public PowerPlan? Plan => _plan;

    /// <summary>Programa a ação, substituindo o plano anterior. <paramref name="seconds"/> de 5 a 86400.</summary>
    public PowerPlan Schedule(PowerAction action, int seconds)
    {
        if (seconds < PowerRules.MinSeconds || seconds > PowerRules.MaxSeconds) throw new ArgumentOutOfRangeException(nameof(seconds));
        var now = _clock();
        var due = now.AddSeconds(seconds);
        DateTimeOffset? warn = seconds > PowerRules.WarnSeconds ? due.AddSeconds(-PowerRules.WarnSeconds) : null;
        _warned = false;
        _plan = new PowerPlan(action, due, warn);
        return _plan;
    }

    /// <summary>Cancela o plano. Devolve falso se não havia nenhum.</summary>
    public bool Cancel()
    {
        if (_plan is null) return false;
        _plan = null;
        _warned = false;
        return true;
    }

    /// <summary>Quanto falta até o próximo evento (aviso ou execução); nulo sem plano. Nunca negativo.</summary>
    public TimeSpan? DelayToNext()
    {
        if (_plan is null) return null;
        var target = !_warned && _plan.WarnAt is { } w ? w : _plan.DueAt;
        var d = target - _clock();
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }

    /// <summary>Confere o relógio. A execução consome o plano; o aviso só sai uma vez.</summary>
    public PowerEvent Poll()
    {
        if (_plan is null) return new PowerEvent(PowerEventKind.None, default);
        var now = _clock();
        if (now >= _plan.DueAt)
        {
            var action = _plan.Action;
            _plan = null;
            _warned = false;
            return new PowerEvent(PowerEventKind.Execute, action);
        }
        if (!_warned && _plan.WarnAt is { } w && now >= w)
        {
            _warned = true;
            return new PowerEvent(PowerEventKind.Warn, _plan.Action);
        }
        return new PowerEvent(PowerEventKind.None, default);
    }
}
