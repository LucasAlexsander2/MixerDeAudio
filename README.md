# Mixer de Áudio

App de bandeja para o Windows 11 (C# / .NET 10 / WinUI 3): um clique no ícone abre uma janelinha com o volume dos apps que estão tocando. Leve por projeto: em repouso fica só o ícone.

## Compilar

```
dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q
dotnet test MixerDeAudio.Tests -v q
dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado
```

Plano e decisões: [docs/PLANO.md](docs/PLANO.md). Estado atual: [docs/ESTADO.md](docs/ESTADO.md).
