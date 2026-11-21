#:package NAudio.Wasapi@3.3.0
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618

// Tocador de teste: toca silêncio na saída padrão (WasapiOut, modo compartilhado) e dá nome à própria sessão.
// Uso: tocar.exe --nome "MX-teste A" --segundos 120 [--midia] [--sem-audio] [--log <arquivo>]
// Fase 7: --midia registra o processo no controle de mídia do Windows (GSMTC) como um player ("Faixa N" de "MX-teste"),
// com anterior, play/pausa e próxima ligados, e anota cada botão recebido numa linha "botao: <Botao>" do arquivo --log.
// --sem-audio não toca nada e fica só no controle de mídia (sem sessão de áudio).
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Media.Playback;

var nome = "MX-teste";
var segundos = 120;
var midia = false;
var semAudio = false;
string? arquivoLog = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--midia") midia = true;
    else if (args[i] == "--sem-audio") semAudio = true;
    else if (i < args.Length - 1 && args[i] == "--nome") nome = args[i + 1];
    else if (i < args.Length - 1 && args[i] == "--segundos") segundos = int.Parse(args[i + 1]);
    else if (i < args.Length - 1 && args[i] == "--log") arquivoLog = args[i + 1];
}

WasapiOut? saida = null;
if (!semAudio)
{
    saida = new WasapiOut(AudioClientShareMode.Shared, 100);
    saida.Init(new SilenceProvider(new WaveFormat(44100, 16, 2)));
    saida.Play();
}

// Fonte de mídia de teste (a mesma ideia da sonda em sondas/fonte.cs).
MediaPlayer? player = null;
if (midia)
{
    SystemMediaTransportControls smtc;
    if (semAudio)
    {
        // Sem MediaPlayer: ele cria uma sessao de audio (inativa), e a linha so de midia nao pode ter linha de som. Usa o
        // controle de midia de uma janela escondida, como os apps comuns.
        var hwnd = Nativo.CreateWindowExW(0, "STATIC", "tocar-midia", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
    }
    else
    {
        player = new MediaPlayer();
        player.CommandManager.IsEnabled = false;
        smtc = player.SystemMediaTransportControls;
    }
    smtc.IsEnabled = true;
    smtc.IsPlayEnabled = true;
    smtc.IsPauseEnabled = true;
    smtc.IsNextEnabled = true;
    smtc.IsPreviousEnabled = true;
    smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
    smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
    smtc.DisplayUpdater.MusicProperties.Title = "Faixa 1";
    smtc.DisplayUpdater.MusicProperties.Artist = "MX-teste";
    smtc.DisplayUpdater.Update();
    var faixa = 1;
    smtc.ButtonPressed += (s, e) =>
    {
        if (arquivoLog is not null)
        {
            try { File.AppendAllText(arquivoLog, $"botao: {e.Button}{Environment.NewLine}"); } catch { }
        }
        switch (e.Button)
        {
            case SystemMediaTransportControlsButton.Play: smtc.PlaybackStatus = MediaPlaybackStatus.Playing; break;
            case SystemMediaTransportControlsButton.Pause: smtc.PlaybackStatus = MediaPlaybackStatus.Paused; break;
            case SystemMediaTransportControlsButton.Next: faixa++; break;
            case SystemMediaTransportControlsButton.Previous: faixa = Math.Max(1, faixa - 1); break;
        }
        smtc.DisplayUpdater.MusicProperties.Title = $"Faixa {faixa}";
        smtc.DisplayUpdater.Update();
    };
}

// Procura a sessão do próprio processo em todas as saídas ativas e define o DisplayName. Fase 4: a saída por app pode
// levar o som para outra saída (e criar uma sessão nova lá), então o nome é reaplicado durante a execução inteira.
var pid = Environment.ProcessId;
using var enumerador = new MMDeviceEnumerator();

bool AplicarNome()
{
    var achou = false;
    if (semAudio) return true;
    using var saidas = enumerador.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    for (var d = 0; d < saidas.Count; d++)
    {
        try
        {
            var gerenciador = saidas[d].AudioSessionManager;
            gerenciador.RefreshSessions();
            var sessoes = gerenciador.Sessions;
            for (var s = 0; s < sessoes.Count; s++)
            {
                var sessao = sessoes[s];
                if (sessao.GetProcessID != pid) continue;
                if (sessao.DisplayName != nome) sessao.DisplayName = nome;
                achou = true;
            }
        }
        catch { }
    }
    return achou;
}

for (var tentativa = 0; tentativa < 30; tentativa++)
{
    if (AplicarNome()) break;
    Thread.Sleep(100);
}

Console.WriteLine(pid);
Console.Out.Flush();
var limite = Stopwatch.StartNew();
while (limite.Elapsed.TotalSeconds < segundos) { Thread.Sleep(250); AplicarNome(); }
saida?.Stop();
player?.Dispose();

static class Nativo
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
}
