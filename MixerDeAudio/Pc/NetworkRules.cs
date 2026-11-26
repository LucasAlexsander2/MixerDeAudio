using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Pc;

/// <summary>Categoria de rede do Windows (<c>NLM_NETWORK_CATEGORY</c>).</summary>
public enum NetworkCategory { Public, Private, Domain }

/// <summary>Uma interface de rede já lida, sem tocar no sistema: é o que as regras de <see cref="NetworkRules"/> avaliam.</summary>
/// <param name="Category">Categoria da rede; nulo quando a leitura falhou ou a interface não está na lista do Windows (tratado como Privada).</param>
public sealed record NicInfo(string Description, NetworkInterfaceType Type, bool IsUp, bool HasGateway, NetworkCategory? Category, IReadOnlyList<IPAddress> Addresses);

/// <summary>
/// Fase 16, D5: quais interfaces podem servir o celular. Permitida é a interface ligada, Ethernet ou Wi-Fi, que não seja
/// virtual nem VPN e cuja rede seja Privada ou de domínio (a Pública não). Lógica pura, sem COM nem rede.
/// </summary>
public static class NetworkRules
{
    private static readonly string[] VirtualWords =
    [
        "Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "WireGuard", "ZeroTier", "Tailscale", "Docker", "Npcap",
    ];

    private static readonly Regex TapWord = new(@"\bTAP\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Valor de <c>NLM_NETWORK_CATEGORY</c> (0 pública, 1 privada, 2 domínio); outro valor vira nulo.</summary>
    public static NetworkCategory? CategoryFromNlm(int value) => value switch
    {
        0 => NetworkCategory.Public,
        1 => NetworkCategory.Private,
        2 => NetworkCategory.Domain,
        _ => null,
    };

    /// <summary>Virtual ou VPN: tipo <c>Tunnel</c>/<c>Ppp</c>, ou descrição com um dos nomes conhecidos de adaptador virtual.</summary>
    public static bool IsVirtual(NetworkInterfaceType type, string? description)
    {
        if (type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) return true;
        if (string.IsNullOrWhiteSpace(description)) return false;
        foreach (var w in VirtualWords)
            if (description.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return TapWord.IsMatch(description);
    }

    /// <summary>Ethernet (de qualquer velocidade) ou Wi-Fi.</summary>
    public static bool IsEthernetOrWifi(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT
        or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211;

    /// <summary>Só a categoria Pública fecha; sem categoria conhecida vale como Privada (a leitura do Windows pode falhar).</summary>
    public static bool IsCategoryAllowed(NetworkCategory? category) => category != NetworkCategory.Public;

    public static bool IsAllowed(NicInfo nic) =>
        nic.IsUp && IsEthernetOrWifi(nic.Type) && !IsVirtual(nic.Type, nic.Description) && IsCategoryAllowed(nic.Category);

    /// <summary>
    /// IPv4 (de faixa privada) das interfaces permitidas, na ordem recebida mas com as que têm gateway antes das outras
    /// (a do Wi-Fi ou cabo da casa). Vazio quando não há rede permitida.
    /// </summary>
    public static IReadOnlyList<IPAddress> AllowedAddresses(IEnumerable<NicInfo> nics)
    {
        var list = new List<IPAddress>();
        foreach (var nic in nics.Where(IsAllowed).OrderByDescending(n => n.HasGateway))
            foreach (var a in nic.Addresses)
                if (a.AddressFamily == AddressFamily.InterNetwork && RemoteRules.IsPrivateNetwork(a) && !IPAddress.IsLoopback(a) && !list.Contains(a))
                    list.Add(a);
        return list;
    }

    /// <summary>
    /// A conexão só vale se o IP local dela (o do PC que o celular alcançou) for de uma interface permitida; o loopback é
    /// sempre aceito.
    /// </summary>
    public static bool LocalAllowed(IPAddress? local, IReadOnlyCollection<IPAddress> allowed)
    {
        if (local is null) return false;
        if (local.IsIPv4MappedToIPv6) local = local.MapToIPv4();
        if (IPAddress.IsLoopback(local)) return true;
        foreach (var a in allowed)
            if (a.Equals(local)) return true;
        return false;
    }

    /// <summary>IP mostrado no QR e no endereço das Configurações: o primeiro permitido, ou nulo.</summary>
    public static IPAddress? PickForQr(IReadOnlyList<IPAddress> allowed) => allowed.Count > 0 ? allowed[0] : null;
}
