#:package NAudio.Wasapi@3.3.0
#:property TargetFramework=net10.0
#:property PublishAot=false
#:property NoWarn=CA1416;CS0618

// Tocador de teste: toca silêncio na saída padrão (WasapiOut, modo compartilhado) e dá nome à própria sessão.
// Uso: tocar.exe --nome "MX-teste A" --segundos 120
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

var nome = "MX-teste";
var segundos = 120;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--nome") nome = args[i + 1];
    else if (args[i] == "--segundos") segundos = int.Parse(args[i + 1]);
}

using var saida = new WasapiOut(AudioClientShareMode.Shared, 100);
saida.Init(new SilenceProvider(new WaveFormat(44100, 16, 2)));
saida.Play();

// Procura a sessão do próprio processo e define o DisplayName.
var pid = Environment.ProcessId;
using var enumerador = new MMDeviceEnumerator();
using var dispositivo = enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
var gerenciador = dispositivo.AudioSessionManager;
for (var tentativa = 0; tentativa < 30; tentativa++)
{
    gerenciador.RefreshSessions();
    var sessoes = gerenciador.Sessions;
    var ok = false;
    for (var s = 0; s < sessoes.Count; s++)
    {
        var sessao = sessoes[s];
        if (sessao.GetProcessID == pid) { sessao.DisplayName = nome; ok = true; }
    }
    if (ok) break;
    Thread.Sleep(100);
}

Console.WriteLine(pid);
Console.Out.Flush();
var limite = Stopwatch.StartNew();
while (limite.Elapsed.TotalSeconds < segundos) Thread.Sleep(200);
saida.Stop();
