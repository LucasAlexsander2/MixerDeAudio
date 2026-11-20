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

// Procura a sessão do próprio processo em todas as saídas ativas e define o DisplayName. Fase 4: a saída por app pode
// levar o som para outra saída (e criar uma sessão nova lá), então o nome é reaplicado durante a execução inteira.
var pid = Environment.ProcessId;
using var enumerador = new MMDeviceEnumerator();

bool AplicarNome()
{
    var achou = false;
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
saida.Stop();
