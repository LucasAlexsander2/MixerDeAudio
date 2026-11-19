namespace MixerDeAudio.Audio;

/// <summary>Estado de uma sessão de áudio (o Windows ainda tem "expirada", que tira a sessão da lista).</summary>
public enum SessionState
{
    Inactive,
    Active,
    Expired,
}

/// <summary>Dados de uma sessão de áudio, já copiados do Windows (sem objetos COM). Lógica pura (D8).</summary>
/// <param name="Id">Identificador da instância da sessão (<c>GetSessionInstanceIdentifier</c>).</param>
/// <param name="ProcessId">Id do processo dono da sessão (0 para os sons do sistema).</param>
/// <param name="ExePath">Caminho do .exe; nulo ou vazio quando não foi possível obter.</param>
/// <param name="Name">Nome já resolvido para mostrar.</param>
/// <param name="Volume">Volume de 0 a 1.</param>
/// <param name="Muted">Se a sessão está muda.</param>
/// <param name="State">Estado da sessão.</param>
/// <param name="IsSystem">Se é a sessão "Sons do sistema".</param>
public sealed record SessionInfo(
    string Id,
    int ProcessId,
    string? ExePath,
    string Name,
    float Volume,
    bool Muted,
    SessionState State,
    bool IsSystem);
