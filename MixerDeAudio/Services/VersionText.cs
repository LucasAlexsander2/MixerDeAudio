using System;

namespace MixerDeAudio.Services;

/// <summary>Texto "Versão x.y.z" das Configurações (Fase 8, D15), a partir da versão do assembly. Parte pura, testada.</summary>
public static class VersionText
{
    public static string Format(Version? version) =>
        version is null ? "Versão desconhecida" : $"Versão {version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
