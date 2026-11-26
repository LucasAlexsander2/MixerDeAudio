using System;
using System.Collections.Generic;
using System.Linq;
using MixerDeAudio.Services;

namespace MixerDeAudio.Remote;

/// <summary>
/// Regras puras da lista de celulares pareados (Fase 16, D11, P3): validade de 30 dias sem uso, limite de 5 e "Esquecer".
/// O relógio entra por parâmetro (<c>now</c>, em UTC), sem arquivo nem tela.
/// </summary>
public static class PhoneListRules
{
    /// <summary>Limite de celulares pareados: o 6º é recusado até alguém ser esquecido.</summary>
    public const int MaxPhones = 5;

    /// <summary>Uma chave sem uso há mais que isso expira.</summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(30);

    /// <summary>A chave expirou: o último uso foi há mais de 30 dias.</summary>
    public static bool IsExpired(DateTime lastUseUtc, DateTime nowUtc) => nowUtc - lastUseUtc > Validity;

    /// <summary>Os celulares ainda válidos (a ordem é mantida). <paramref name="removed"/> diz quantos expiraram.</summary>
    public static List<PairedPhone> RemoveExpired(IEnumerable<PairedPhone> phones, DateTime nowUtc, out int removed)
    {
        var list = phones.ToList();
        var valid = list.Where(p => !IsExpired(p.UltimoUso, nowUtc)).ToList();
        removed = list.Count - valid.Count;
        return valid;
    }

    /// <summary>
    /// O limite já foi atingido? Só contam os celulares válidos (os expirados saem na hora de parear). Conferido no
    /// <c>/parear</c> antes da confirmação no PC.
    /// </summary>
    public static bool LimitReached(IEnumerable<PairedPhone> phones, DateTime nowUtc) =>
        phones.Count(p => !IsExpired(p.UltimoUso, nowUtc)) >= MaxPhones;

    /// <summary>A lista sem o celular de <paramref name="hash"/> (nada muda se ele não existir).</summary>
    public static List<PairedPhone> Forget(IEnumerable<PairedPhone> phones, string hash) =>
        phones.Where(p => !string.Equals(p.Hash, hash, StringComparison.Ordinal)).ToList();

    /// <summary>"agora", "há 5 min", "há 3 h", "ontem", "há 12 dias": o último uso em texto curto.</summary>
    public static string LastUseText(DateTime lastUseUtc, DateTime nowUtc)
    {
        var d = nowUtc - lastUseUtc;
        if (d < TimeSpan.FromMinutes(1)) return "agora";
        if (d < TimeSpan.FromHours(1)) return $"há {(int)d.TotalMinutes} min";
        if (d < TimeSpan.FromDays(1)) return $"há {(int)d.TotalHours} h";
        var days = (int)d.TotalDays;
        return days == 1 ? "ontem" : $"há {days} dias";
    }
}
