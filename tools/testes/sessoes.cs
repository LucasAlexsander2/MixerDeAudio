#:package NAudio.Wasapi@3.3.0
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618

// Ferramenta de teste das sessões de áudio (todas as saídas ativas).
//   sessoes.exe listar                          -> uma linha por sessão: pid|nome|estado|volume|mudo
//   sessoes.exe definir <pid> <volume> [mudo|som] -> muda a sessão daquele processo (volume de 0 a 1)
// SEGURANÇA: "definir" só mexe em tocadores de teste (DisplayName começando com "MX-teste"); recusa qualquer outro app.
using System.Globalization;
using NAudio.CoreAudioApi;

if (args.Length == 0) { Console.Error.WriteLine("uso: listar | definir <pid> <volume> [mudo|som]"); return 2; }

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

    default:
        Console.Error.WriteLine("comando desconhecido");
        return 2;
}
