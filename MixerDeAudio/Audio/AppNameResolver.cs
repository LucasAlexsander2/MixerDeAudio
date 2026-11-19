namespace MixerDeAudio.Audio;

/// <summary>Escolhe o nome mostrado entre os candidatos já obtidos (D13). Lógica pura: a leitura do .exe fica fora.</summary>
public static class AppNameResolver
{
    public const string SystemName = "Sons do sistema";
    public const string UnknownName = "App desconhecido";

    /// <param name="isSystem">Sessão dos sons do sistema.</param>
    /// <param name="displayName">DisplayName da sessão, já com o texto <c>@</c> resolvido (nulo se não resolveu).</param>
    /// <param name="fileDescription">Descrição do .exe.</param>
    /// <param name="processName">Nome do processo.</param>
    public static string Resolve(bool isSystem, string? displayName, string? fileDescription, string? processName)
    {
        if (isSystem) return SystemName;
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        if (!string.IsNullOrWhiteSpace(fileDescription)) return fileDescription.Trim();
        if (!string.IsNullOrWhiteSpace(processName)) return processName.Trim();
        return UnknownName;
    }
}
