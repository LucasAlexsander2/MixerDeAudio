using System;
using System.Collections.Generic;

namespace MixerDeAudio.Audio;

/// <summary>O que fazer com um clique num botão de saída de um app (D7).</summary>
public enum RouteActionKind
{
    /// <summary>Nada: a saída clicada já é a efetiva.</summary>
    None,
    /// <summary>Apagar a escolha do app: ele volta a seguir a saída padrão (P1).</summary>
    Clear,
    /// <summary>Gravar a saída clicada como escolha do app.</summary>
    Set,
}

/// <summary>Resultado de <see cref="AppRouting.OnClick"/>; <c>DeviceId</c> só vale em <see cref="RouteActionKind.Set"/>.</summary>
public readonly record struct RouteAction(RouteActionKind Kind, string? DeviceId = null);

/// <summary>Lógica pura da saída por app (D2 e D7): conversão de ids e regras de destaque e de clique.</summary>
public static class AppRouting
{
    private const string PolicyPrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    /// <summary>Caminho que a interface de política do Windows usa, a partir do id do <c>MMDevice</c> (D2).</summary>
    public static string ToPolicyId(string mmDeviceId) => PolicyPrefix + mmDeviceId + RenderSuffix;

    /// <summary>
    /// Id do <c>MMDevice</c> a partir do caminho da interface de política. Tolera maiúsculas e minúsculas; devolve nulo
    /// para texto vazio ou fora do formato (D2).
    /// </summary>
    public static string? FromPolicyId(string? policyId)
    {
        if (string.IsNullOrWhiteSpace(policyId)) return null;
        var s = policyId.Trim();
        if (s.Length <= PolicyPrefix.Length + RenderSuffix.Length) return null;
        if (!s.StartsWith(PolicyPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (!s.EndsWith(RenderSuffix, StringComparison.OrdinalIgnoreCase)) return null;
        var middle = s.Substring(PolicyPrefix.Length, s.Length - PolicyPrefix.Length - RenderSuffix.Length);
        return string.IsNullOrWhiteSpace(middle) ? null : middle;
    }

    /// <summary>
    /// Saída efetiva do app: a escolhida, se estiver entre as ativas; senão a padrão (se a escolhida foi desligada, o
    /// Windows manda o som para a padrão). Nulo se não houver padrão.
    /// </summary>
    public static string? Effective(string? preferredId, string? defaultId, IEnumerable<string> activeIds)
    {
        if (!string.IsNullOrEmpty(preferredId))
        {
            foreach (var id in activeIds)
                if (string.Equals(id, preferredId, StringComparison.OrdinalIgnoreCase)) return id;
        }
        return string.IsNullOrEmpty(defaultId) ? null : defaultId;
    }

    /// <summary>Decide o que o clique faz (D7, P1).</summary>
    public static RouteAction OnClick(string clickedId, string? preferredId, string? defaultId)
    {
        var effective = !string.IsNullOrEmpty(preferredId) ? preferredId : defaultId;
        if (string.Equals(clickedId, effective, StringComparison.OrdinalIgnoreCase)) return new RouteAction(RouteActionKind.None);
        if (string.Equals(clickedId, defaultId, StringComparison.OrdinalIgnoreCase)) return new RouteAction(RouteActionKind.Clear);
        return new RouteAction(RouteActionKind.Set, clickedId);
    }

    /// <summary>Os botões só aparecem com 2 saídas ou mais, num app com processo e com o serviço disponível (D7).</summary>
    public static bool ShowButtons(bool isSystem, bool hasProcess, int outputCount, bool serviceOk) =>
        !isSystem && hasProcess && serviceOk && outputCount >= 2;
}
