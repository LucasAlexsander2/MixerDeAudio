# Mixer de Áudio

App de bandeja para o Windows 11 (C# / .NET 10 / WinUI 3): um clique no ícone abre uma janelinha com o volume dos apps que estão tocando. Leve por projeto: em repouso fica só o ícone.

## Compilar

```
dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q
dotnet test MixerDeAudio.Tests -v q
dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado
```

## Controle do PC pelo celular (Fase 9)

Com o controle pelo celular ligado (Configurações), a página do celular, na mesma rede de casa, ganha ferramentas para usar o PC à distância. Só um celular fica conectado por vez, e **cada grupo de ferramentas tem um interruptor nas Configurações, desligado por padrão**: energia (desligar, reiniciar, suspender, bloquear, com timer), monitores (projeção, apagar e acordar as telas), entrada (touchpad, texto, teclas, painel de apresentação e vídeo, manter a tela ligada), tela (imagem do PC, só depois de tocar em "Mostrar a tela"), janelas, atalhos (pasta `%LOCALAPPDATA%\MixerDeAudio\Atalhos`), transferência (área de transferência só de texto nos dois sentidos e envio de arquivo, até 500 MB, para `Downloads\Do celular`) e estado do PC (CPU, memória, tempo ligado, bateria e disco, mais o timer de sono da música). Com o celular desconectado ou o grupo desligado, nada novo roda.

Para testes automáticos existe `--teste-pc`: nada é feito no PC de verdade, as ações só são gravadas no log (`teste-pc: ...`), a área de transferência fica em memória, os arquivos recebidos vão para `%TEMP%\mixer-check\recebidos` (limite de 5 MB) e os atalhos para `%TEMP%\mixer-check\atalhos`. Detalhes em [docs/fases/fase-9.md](docs/fases/fase-9.md).

Plano e decisões: [docs/PLANO.md](docs/PLANO.md). Estado atual: [docs/ESTADO.md](docs/ESTADO.md).
