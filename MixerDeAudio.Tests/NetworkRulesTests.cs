using System.Net;
using System.Net.NetworkInformation;
using MixerDeAudio.Pc;

namespace MixerDeAudio.Tests;

/// <summary>Fase 16, D5: quais interfaces podem servir o celular (lógica pura, sem COM nem rede).</summary>
public class NetworkRulesTests
{
    private static NicInfo Nic(string desc = "Intel(R) Wi-Fi 6 AX201", NetworkInterfaceType type = NetworkInterfaceType.Wireless80211,
        bool up = true, bool gateway = true, NetworkCategory? category = NetworkCategory.Private, string[]? ips = null) =>
        new(desc, type, up, gateway, category, (ips ?? ["192.168.0.10"]).Select(IPAddress.Parse).ToList());

    [Theory]
    [InlineData(NetworkInterfaceType.Tunnel, "Teredo Tunneling Pseudo-Interface", true)]
    [InlineData(NetworkInterfaceType.Ppp, "VPN da empresa", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "Hyper-V Virtual Ethernet Adapter", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "vEthernet (WSL)", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "VirtualBox Host-Only Ethernet Adapter", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "VMware Virtual Ethernet Adapter for VMnet8", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "TAP-Windows Adapter V9", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "WireGuard Tunnel", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "ZeroTier Virtual Port", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "Tailscale Tunnel", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "Docker Desktop Network", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "Npcap Loopback Adapter", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "Realtek PCIe GbE Family Controller", false)]
    [InlineData(NetworkInterfaceType.Wireless80211, "Intel(R) Wi-Fi 6 AX201 160MHz", false)]
    [InlineData(NetworkInterfaceType.Ethernet, "Intel(R) Ethernet Connection (7) I219-V", false)]
    [InlineData(NetworkInterfaceType.Ethernet, null, false)]
    public void Virtual_PeloTipoOuPelaDescricao(NetworkInterfaceType tipo, string? descricao, bool esperado) =>
        Assert.Equal(esperado, NetworkRules.IsVirtual(tipo, descricao));

    [Theory]
    [InlineData(0, NetworkCategory.Public)]
    [InlineData(1, NetworkCategory.Private)]
    [InlineData(2, NetworkCategory.Domain)]
    public void CategoriaDoWindows(int valor, NetworkCategory esperado) => Assert.Equal(esperado, NetworkRules.CategoryFromNlm(valor));

    [Fact]
    public void CategoriaDesconhecida_ViraNula() => Assert.Null(NetworkRules.CategoryFromNlm(7));

    [Fact]
    public void RedePrivadaOuDeDominio_EhPermitida_PublicaNao_SemCategoriaVaiComoPrivada()
    {
        Assert.True(NetworkRules.IsAllowed(Nic(category: NetworkCategory.Private)));
        Assert.True(NetworkRules.IsAllowed(Nic(category: NetworkCategory.Domain)));
        Assert.False(NetworkRules.IsAllowed(Nic(category: NetworkCategory.Public)));
        Assert.True(NetworkRules.IsAllowed(Nic(category: null))); // leitura falhou: Privada
    }

    [Fact]
    public void InterfaceDesligada_Virtual_OuQueNaoEhEthernetNemWifi_NaoEhPermitida()
    {
        Assert.False(NetworkRules.IsAllowed(Nic(up: false)));
        Assert.False(NetworkRules.IsAllowed(Nic(desc: "vEthernet (Default Switch)", type: NetworkInterfaceType.Ethernet)));
        Assert.False(NetworkRules.IsAllowed(Nic(type: NetworkInterfaceType.Tunnel)));
        Assert.False(NetworkRules.IsAllowed(Nic(type: NetworkInterfaceType.Loopback)));
        Assert.False(NetworkRules.IsAllowed(Nic(type: NetworkInterfaceType.Wwanpp)));
        Assert.True(NetworkRules.IsAllowed(Nic(desc: "Realtek PCIe GbE", type: NetworkInterfaceType.GigabitEthernet)));
    }

    [Fact]
    public void EnderecosPermitidos_SoIPv4Privado_ComGatewayPrimeiro()
    {
        var semGateway = Nic(desc: "Realtek PCIe GbE", type: NetworkInterfaceType.Ethernet, gateway: false, ips: ["10.0.0.7"]);
        var comGateway = Nic(ips: ["fe80::1", "192.168.0.10"]);
        var publica = Nic(category: NetworkCategory.Public, ips: ["192.168.50.2"]);
        var virt = Nic(desc: "vEthernet (WSL)", type: NetworkInterfaceType.Ethernet, ips: ["172.20.0.1"]);
        var ipPublico = Nic(ips: ["8.8.4.4"]);
        var lista = NetworkRules.AllowedAddresses([semGateway, comGateway, publica, virt, ipPublico]);
        Assert.Equal(["192.168.0.10", "10.0.0.7"], lista.Select(a => a.ToString()).ToArray());
        Assert.Equal("192.168.0.10", NetworkRules.PickForQr(lista)!.ToString());
    }

    [Fact]
    public void SemInterfacePermitida_ListaVazia_ESemIpParaOQr()
    {
        var lista = NetworkRules.AllowedAddresses([Nic(category: NetworkCategory.Public), Nic(up: false)]);
        Assert.Empty(lista);
        Assert.Null(NetworkRules.PickForQr(lista));
    }

    [Theory]
    [InlineData("192.168.0.10", true)]
    [InlineData("::ffff:192.168.0.10", true)]   // IPv6 mapeado vale pelo IPv4
    [InlineData("127.0.0.1", true)]             // o loopback sempre passa
    [InlineData("::1", true)]
    [InlineData("192.168.0.99", false)]
    [InlineData("10.0.0.7", false)]
    [InlineData("172.20.0.1", false)]
    public void ConexaoSoValeComOIpLocalPermitido(string local, bool esperado)
    {
        IReadOnlyCollection<IPAddress> permitidos = [IPAddress.Parse("192.168.0.10")];
        Assert.Equal(esperado, NetworkRules.LocalAllowed(IPAddress.Parse(local), permitidos));
    }

    [Fact]
    public void ConexaoSemIpLocal_EhRecusada() => Assert.False(NetworkRules.LocalAllowed(null, [IPAddress.Parse("192.168.0.10")]));
}
