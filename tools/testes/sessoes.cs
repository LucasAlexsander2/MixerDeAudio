#:package NAudio.Wasapi@3.3.0
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618;SYSLIB1096

// Ferramenta de teste das sessões de áudio (todas as saídas ativas).
//   sessoes.exe listar                          -> uma linha por sessão: pid|nome|estado|volume|mudo
//   sessoes.exe definir <pid> <volume> [mudo|som] -> muda a sessão daquele processo (volume de 0 a 1)
//   sessoes.exe dispositivos                    -> uma linha por saída ativa: id|nome curto|nome completo|formato|padrao(sim/nao)|volume|mudo
//   sessoes.exe geral <volume>                  -> muda o volume geral da saída padrão (0 a 1)
//   sessoes.exe rota-ler <pid>                  -> id do MMDevice que o processo escolheu, ou "padrao" (só leitura, vale para qualquer app)
//   sessoes.exe rota <pid> <id|padrao>          -> grava (ou apaga, com "padrao") a saída escolhida do processo
// SEGURANÇA: "definir" só mexe em tocadores de teste (DisplayName começando com "MX-teste"); recusa qualquer outro app.
// "rota" só mexe em tocadores de teste: recusa (código 3) qualquer pid cujo processo não se chame "tocar" ou "tocar-b".
// Nunca declara nem chama ClearAllPersistedApplicationDefaultEndpoints (apagaria a escolha de todos os apps).
// "geral" recusa um valor que fique a mais de 0,02 do volume atual (só o toque mínimo). Nunca troca a saída padrão nem mexe no mudo geral.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

if (args.Length == 0) { Console.Error.WriteLine("uso: listar | definir <pid> <volume> [mudo|som] | dispositivos | geral <volume> | rota-ler <pid> | rota <pid> <id|padrao>"); return 2; }

using var enumerador = new MMDeviceEnumerator();
using var dispositivos = enumerador.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

switch (args[0])
{
    case "listar":
        for (var d = 0; d < dispositivos.Count; d++)
        {
            var gerenciador = dispositivos[d].AudioSessionManager;
            var sessoes = gerenciador.Sessions;
            for (var s = 0; s < sessoes.Count; s++)
            {
                var sessao = sessoes[s];
                var nome = sessao.IsSystemSoundsSession ? "Sons do sistema" : sessao.DisplayName;
                Console.WriteLine(string.Join('|', sessao.GetProcessID, nome, sessao.State.ToString().Replace("AudioSessionState", ""),
                    sessao.SimpleAudioVolume.Volume.ToString("0.00", CultureInfo.InvariantCulture),
                    sessao.SimpleAudioVolume.Mute ? "sim" : "nao",
                    dispositivos[d].ID));
            }
        }
        return 0;

    case "definir":
        if (args.Length < 3 || !uint.TryParse(args[1], out var pid)
            || !float.TryParse(args[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
        {
            Console.Error.WriteLine("uso: definir <pid> <volume 0-1> [mudo|som]");
            return 2;
        }
        var mexeu = 0;
        for (var d = 0; d < dispositivos.Count; d++)
        {
            var sessoes = dispositivos[d].AudioSessionManager.Sessions;
            for (var s = 0; s < sessoes.Count; s++)
            {
                var sessao = sessoes[s];
                if (sessao.GetProcessID != pid) continue;
                if (!(sessao.DisplayName ?? "").StartsWith("MX-teste", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"RECUSADO: o processo {pid} nao e um tocador de teste.");
                    return 3;
                }
                sessao.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                if (args.Length > 3) sessao.SimpleAudioVolume.Mute = args[3] == "mudo";
                mexeu++;
            }
        }
        if (mexeu == 0) { Console.Error.WriteLine($"processo {pid} sem sessao"); return 4; }
        return 0;

    case "dispositivos":
    {
        string? idPadrao = null;
        try { using var padrao = enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); idPadrao = padrao.ID; }
        catch { }
        for (var d = 0; d < dispositivos.Count; d++)
        {
            var dev = dispositivos[d];
            string? desc = null, completo = null; var formato = 0;
            try
            {
                var props = dev.Properties;
                if (props.Contains(PropertyKeys.PKEY_Device_DeviceDesc)) desc = props[PropertyKeys.PKEY_Device_DeviceDesc].Value as string;
                if (props.Contains(PropertyKeys.PKEY_Device_FriendlyName)) completo = props[PropertyKeys.PKEY_Device_FriendlyName].Value as string;
                if (props.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor) && props[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value is { } ff) formato = Convert.ToInt32(ff);
            }
            catch { }
            // Mesma regra do app (D6): descrição, depois nome completo, depois "Saída desconhecida".
            var curto = !string.IsNullOrWhiteSpace(desc) ? desc.Trim() : !string.IsNullOrWhiteSpace(completo) ? completo.Trim() : "Saída desconhecida";
            var nomeCompleto = !string.IsNullOrWhiteSpace(completo) ? completo.Trim() : curto;
            var vol = dev.AudioEndpointVolume;
            Console.WriteLine(string.Join('|', dev.ID, curto, nomeCompleto, formato,
                dev.ID == idPadrao ? "sim" : "nao",
                vol.MasterVolumeLevelScalar.ToString("0.000", CultureInfo.InvariantCulture),
                vol.Mute ? "sim" : "nao"));
        }
        return 0;
    }

    case "geral":
    {
        if (args.Length < 2 || !float.TryParse(args[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var novo))
        {
            Console.Error.WriteLine("uso: geral <volume 0-1>");
            return 2;
        }
        using var padrao = enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var vol = padrao.AudioEndpointVolume;
        var atual = vol.MasterVolumeLevelScalar;
        if (Math.Abs(novo - atual) > 0.0201f)
        {
            Console.Error.WriteLine($"RECUSADO: {novo.ToString("0.000", CultureInfo.InvariantCulture)} fica a mais de 0,02 do volume geral atual ({atual.ToString("0.000", CultureInfo.InvariantCulture)}).");
            return 3;
        }
        vol.MasterVolumeLevelScalar = Math.Clamp(novo, 0f, 1f);
        return 0;
    }

    case "rota-ler":
    {
        if (args.Length < 2 || !uint.TryParse(args[1], out var pidLer)) { Console.Error.WriteLine("uso: rota-ler <pid>"); return 2; }
        var lido = Rota.Ler(pidLer);
        Console.WriteLine(lido ?? "padrao");
        return 0;
    }

    case "rota":
    {
        if (args.Length < 3 || !uint.TryParse(args[1], out var pidRota)) { Console.Error.WriteLine("uso: rota <pid> <id|padrao>"); return 2; }
        string? nomeProc;
        try { nomeProc = Process.GetProcessById((int)pidRota).ProcessName; }
        catch { Console.Error.WriteLine($"RECUSADO: processo {pidRota} nao existe."); return 3; }
        if (nomeProc != "tocar" && nomeProc != "tocar-b")
        {
            Console.Error.WriteLine($"RECUSADO: o processo {pidRota} ('{nomeProc}') nao e um tocador de teste.");
            return 3;
        }
        Rota.Gravar(pidRota, args[2] == "padrao" ? null : args[2]);
        return 0;
    }

    default:
        Console.Error.WriteLine("comando desconhecido");
        return 2;
}

// ---- Saída por app (IAudioPolicyConfigFactory), a mesma interface do app; ver docs/fases/fase-4.md, D1 ----
[ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioPolicyConfigFactory
{
    // IInspectable (3) + 19 métodos que não são usados, só ocupam a posição
    [PreserveSig] int M01(); [PreserveSig] int M02(); [PreserveSig] int M03(); [PreserveSig] int M04(); [PreserveSig] int M05();
    [PreserveSig] int M06(); [PreserveSig] int M07(); [PreserveSig] int M08(); [PreserveSig] int M09(); [PreserveSig] int M10();
    [PreserveSig] int M11(); [PreserveSig] int M12(); [PreserveSig] int M13(); [PreserveSig] int M14(); [PreserveSig] int M15();
    [PreserveSig] int M16(); [PreserveSig] int M17(); [PreserveSig] int M18(); [PreserveSig] int M19(); [PreserveSig] int M20();
    [PreserveSig] int M21(); [PreserveSig] int M22();
    [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr hstring);
    [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr hstring);
    // ClearAllPersistedApplicationDefaultEndpoints: NAO declarado, de propósito.
}

static class Rota
{
    const string Prefixo = @"\\?\SWD#MMDEVAPI#";
    const string Sufixo = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int len, out IntPtr h);
    [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr h, out uint len);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr h);

    static IAudioPolicyConfigFactory Abrir()
    {
        var iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
        const string nome = "Windows.Media.Internal.AudioPolicyConfig";
        Marshal.ThrowExceptionForHR(WindowsCreateString(nome, nome.Length, out var hClasse));
        try
        {
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hClasse, ref iid, out var ptr));
            try { return (IAudioPolicyConfigFactory)Marshal.GetObjectForIUnknown(ptr); }
            finally { Marshal.Release(ptr); }
        }
        finally { WindowsDeleteString(hClasse); }
    }

    public static string? Ler(uint pid)
    {
        var f = Abrir();
        Marshal.ThrowExceptionForHR(f.GetPersistedDefaultAudioEndpoint(pid, 0, 1, out var h));
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = WindowsGetStringRawBuffer(h, out var len);
            if (buf == IntPtr.Zero || len == 0) return null;
            var texto = Marshal.PtrToStringUni(buf, (int)len)!;
            if (texto.StartsWith(Prefixo, StringComparison.OrdinalIgnoreCase) && texto.EndsWith(Sufixo, StringComparison.OrdinalIgnoreCase))
                return texto.Substring(Prefixo.Length, texto.Length - Prefixo.Length - Sufixo.Length);
            return texto;
        }
        finally { WindowsDeleteString(h); }
    }

    public static void Gravar(uint pid, string? idMm)
    {
        var f = Abrir();
        var h = IntPtr.Zero;
        try
        {
            if (idMm is not null)
            {
                var caminho = Prefixo + idMm + Sufixo;
                Marshal.ThrowExceptionForHR(WindowsCreateString(caminho, caminho.Length, out h));
            }
            Marshal.ThrowExceptionForHR(f.SetPersistedDefaultAudioEndpoint(pid, 0, 0, h));
            Marshal.ThrowExceptionForHR(f.SetPersistedDefaultAudioEndpoint(pid, 0, 1, h));
        }
        finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
    }
}
