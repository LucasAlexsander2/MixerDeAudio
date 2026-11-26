using System.Text.Json;
using MixerDeAudio.Pc;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

/// <summary>Fase 9, parte 9d: estado do PC (D27): CPU a partir de pares de tempos, bateria, a mensagem <c>info</c> e a assinatura.</summary>
public class PcInfoTests
{
    [Fact]
    public void Cpu_MetadeOcupada_Cinquenta()
    {
        // intervalo: kernel +100 (já inclui 50 de ocioso), user +0 => ocupado 50 de 100
        var a = new CpuTimes(Idle: 1000, Kernel: 2000, User: 500);
        var b = new CpuTimes(Idle: 1050, Kernel: 2100, User: 500);
        Assert.Equal(50, PcInfoRules.CpuPercent(a, b));
    }

    [Fact]
    public void Cpu_TudoOcioso_Zero()
    {
        var a = new CpuTimes(100, 200, 50);
        var b = new CpuTimes(200, 300, 50);
        Assert.Equal(0, PcInfoRules.CpuPercent(a, b));
    }

    [Fact]
    public void Cpu_NadaOcioso_Cem()
    {
        var a = new CpuTimes(100, 200, 50);
        var b = new CpuTimes(100, 260, 90);
        Assert.Equal(100, PcInfoRules.CpuPercent(a, b));
    }

    [Fact]
    public void Cpu_SemIntervalo_Zero()
    {
        var a = new CpuTimes(100, 200, 50);
        Assert.Equal(0, PcInfoRules.CpuPercent(a, a));
    }

    [Fact]
    public void Cpu_ContadorQueVoltou_Zero()
    {
        var a = new CpuTimes(100, 200, 50);
        var b = new CpuTimes(90, 150, 40);
        Assert.Equal(0, PcInfoRules.CpuPercent(a, b));
    }

    [Fact]
    public void Cpu_OciosoMaiorQueTotal_NaoPassaDeZero()
    {
        var a = new CpuTimes(0, 0, 0);
        var b = new CpuTimes(500, 100, 0); // dados incoerentes
        Assert.Equal(0, PcInfoRules.CpuPercent(a, b));
    }

    [Fact]
    public void Cpu_Arredonda()
    {
        // ocupado 1 de 3 = 33,3%
        var a = new CpuTimes(0, 0, 0);
        var b = new CpuTimes(2, 3, 0);
        Assert.Equal(33, PcInfoRules.CpuPercent(a, b));
    }

    [Fact]
    public void Cpu_PrimeiraLeitura_MediaDesdeOBoot()
    {
        var boot = new CpuTimes(9000, 10000, 1000); // total 11000, ocioso 9000 => 18%
        Assert.Equal(18, PcInfoRules.CpuPercent(default, boot));
    }

    [Theory]
    [InlineData(128, 255, null)] // sem bateria
    [InlineData(255, 80, null)]  // desconhecido
    [InlineData(1, 255, null)]   // % desconhecido
    [InlineData(1, 80, 80)]
    [InlineData(9, 100, 100)]
    [InlineData(4, 0, 0)]
    public void Bateria_Percentual(byte flag, byte pct, int? esperado) => Assert.Equal(esperado, PcInfoRules.BatteryPercent(flag, pct));

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(1, false)]
    [InlineData(255, false)]
    public void Bateria_Carregando(byte flag, bool esperado) => Assert.Equal(esperado, PcInfoRules.IsCharging(flag));

    // ---- Mensagem info ----

    [Fact]
    public void BuildInfo_ComBateria()
    {
        var snap = new PcInfoSnapshot(37, 8_000_000_000, 16_000_000_000, 3600, 64, true, 120_000_000_000, 500_000_000_000);
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildInfo(snap));
        var r = doc.RootElement;
        Assert.Equal("info", r.GetProperty("t").GetString());
        Assert.Equal(37, r.GetProperty("cpu").GetInt32());
        Assert.Equal(8_000_000_000, r.GetProperty("mem").GetProperty("usada").GetInt64());
        Assert.Equal(16_000_000_000, r.GetProperty("mem").GetProperty("total").GetInt64());
        Assert.Equal(3600, r.GetProperty("ligado").GetInt64());
        Assert.Equal(64, r.GetProperty("bateria").GetProperty("pct").GetInt32());
        Assert.True(r.GetProperty("bateria").GetProperty("carregando").GetBoolean());
        Assert.Equal(120_000_000_000, r.GetProperty("disco").GetProperty("livre").GetInt64());
        Assert.Equal(500_000_000_000, r.GetProperty("disco").GetProperty("total").GetInt64());
    }

    [Fact]
    public void BuildInfo_SemBateria_Nula()
    {
        var snap = new PcInfoSnapshot(5, 1, 2, 3, null, false, 4, 5);
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildInfo(snap));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("bateria").ValueKind);
    }

    [Fact]
    public void BuildInfo_CortaCpuForaDaFaixa()
    {
        using var hi = JsonDocument.Parse(RemoteProtocol.BuildInfo(new PcInfoSnapshot(250, 0, 0, 0, null, false, 0, 0)));
        Assert.Equal(100, hi.RootElement.GetProperty("cpu").GetInt32());
        using var lo = JsonDocument.Parse(RemoteProtocol.BuildInfo(new PcInfoSnapshot(-4, 0, 0, 0, null, false, 0, 0)));
        Assert.Equal(0, lo.RootElement.GetProperty("cpu").GetInt32());
    }

    // ---- GPU (Fase 15a, D13) ----

    private static KeyValuePair<string, double> Motor(string luid, int eng, string tipo, double valor, int pid = 100) =>
        new($"pid_{pid}_luid_0x00000000_{luid}_phys_0_eng_{eng}_engtype_{tipo}", valor);

    [Fact]
    public void Gpu_SomaOsMotores3DDoMesmoAdaptador()
    {
        var lista = new[] { Motor("0x0000AAAA", 0, "3D", 10.4), Motor("0x0000AAAA", 0, "3D", 20.2, pid: 200), Motor("0x0000AAAA", 1, "3D", 5.0) };
        Assert.Equal(36, PcInfoRules.GpuPercent(lista)); // 35,6 arredonda para 36
    }

    [Fact]
    public void Gpu_IgnoraOutrosTiposDeMotor()
    {
        var lista = new[] { Motor("0x0000AAAA", 0, "3D", 12), Motor("0x0000AAAA", 1, "VideoDecode", 80), Motor("0x0000AAAA", 2, "Copy", 90), Motor("0x0000AAAA", 3, "Compute_0", 70) };
        Assert.Equal(12, PcInfoRules.GpuPercent(lista));
    }

    [Fact]
    public void Gpu_ValeOAdaptadorMaisUsado()
    {
        var lista = new[] { Motor("0x0000AAAA", 0, "3D", 8), Motor("0x0000BBBB", 0, "3D", 30), Motor("0x0000BBBB", 1, "3D", 25), Motor("0x0000CCCC", 0, "3D", 40) };
        Assert.Equal(55, PcInfoRules.GpuPercent(lista)); // BBBB soma 55; não soma entre adaptadores
    }

    [Fact]
    public void Gpu_CortaEmCemEIgnoraNegativosENaoNumeros()
    {
        var lista = new[] { Motor("0x0000AAAA", 0, "3D", 70), Motor("0x0000AAAA", 1, "3D", 60), Motor("0x0000AAAA", 2, "3D", -5), Motor("0x0000AAAA", 3, "3D", double.NaN) };
        Assert.Equal(100, PcInfoRules.GpuPercent(lista));
    }

    [Fact]
    public void Gpu_SemMotores3D_SemLeitura()
    {
        Assert.Null(PcInfoRules.GpuPercent(Array.Empty<KeyValuePair<string, double>>()));
        Assert.Null(PcInfoRules.GpuPercent(new[] { Motor("0x0000AAAA", 0, "Copy", 50) }));
        Assert.Null(PcInfoRules.GpuPercent(new[] { Motor("0x0000AAAA", 0, "3D", double.PositiveInfinity) }));
    }

    [Fact]
    public void Gpu_3DParado_ZeroEMaiusculasNoTipo()
    {
        Assert.Equal(0, PcInfoRules.GpuPercent(new[] { Motor("0x0000AAAA", 0, "3D", 0) }));
        Assert.Equal(7, PcInfoRules.GpuPercent(new[] { new KeyValuePair<string, double>("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3d", 7) }));
    }

    [Fact]
    public void BuildInfo_ComGpu_VaiOCampo()
    {
        var snap = new PcInfoSnapshot(5, 1, 2, 3, null, false, 4, 5, 42);
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildInfo(snap));
        Assert.Equal(42, doc.RootElement.GetProperty("gpu").GetInt32());
    }

    [Fact]
    public void BuildInfo_SemGpu_CampoAusente()
    {
        using var doc = JsonDocument.Parse(RemoteProtocol.BuildInfo(new PcInfoSnapshot(5, 1, 2, 3, null, false, 4, 5)));
        Assert.False(doc.RootElement.TryGetProperty("gpu", out _));
        Assert.True(doc.RootElement.TryGetProperty("cpu", out _)); // o resto do info continua igual
    }

    [Fact]
    public void BuildInfo_GpuForaDaFaixa_Corta()
    {
        using var hi = JsonDocument.Parse(RemoteProtocol.BuildInfo(new PcInfoSnapshot(1, 0, 0, 0, null, false, 0, 0, 180)));
        Assert.Equal(100, hi.RootElement.GetProperty("gpu").GetInt32());
        using var lo = JsonDocument.Parse(RemoteProtocol.BuildInfo(new PcInfoSnapshot(1, 0, 0, 0, null, false, 0, 0, -3)));
        Assert.Equal(0, lo.RootElement.GetProperty("gpu").GetInt32());
    }

    // ---- Assinatura ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Assinar_Info_PertenceAoGrupoEstado(bool on)
    {
        var json = "{\"t\":\"assinar\",\"g\":\"info\",\"on\":" + (on ? "true" : "false") + "}";
        Assert.Equal(ParseStatus.Ok, RemoteProtocol.ParseCommand(json, out var c));
        var sub = Assert.IsType<SubscribeCommand>(c);
        Assert.Equal("info", sub.Group);
        Assert.Equal(on, sub.On);
        Assert.Equal(RemoteTools.Info, RemoteCommandGroups.GroupOf(c!));
    }
}
