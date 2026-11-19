#:package NAudio.Wasapi@3.3.0
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618

// Ferramenta de teste das sessões de áudio (todas as saídas ativas).
//   sessoes.exe listar                          -> uma linha por sessão: pid|nome|estado|volume|mudo
//   sessoes.exe definir <pid> <volume> [mudo|som] -> muda a sessão daquele processo (volume de 0 a 1)
//   sessoes.exe dispositivos                    -> uma linha por saída ativa: id|nome curto|nome completo|formato|padrao(sim/nao)|volume|mudo
//   sessoes.exe geral <volume>                  -> muda o volume geral da saída padrão (0 a 1)
// SEGURANÇA: "definir" só mexe em tocadores de teste (DisplayName começando com "MX-teste"); recusa qualquer outro app.
// "geral" recusa um valor que fique a mais de 0,02 do volume atual (só o toque mínimo). Nunca troca a saída padrão nem mexe no mudo geral.
using System.Globalization;
using NAudio.CoreAudioApi;

if (args.Length == 0) { Console.Error.WriteLine("uso: listar | definir <pid> <volume> [mudo|som] | dispositivos | geral <volume>"); return 2; }

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
                    sessao.SimpleAudioVolume.Mute ? "sim" : "nao"));
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

    default:
        Console.Error.WriteLine("comando desconhecido");
        return 2;
}
