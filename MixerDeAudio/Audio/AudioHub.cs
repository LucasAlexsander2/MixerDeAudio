using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Services;

namespace MixerDeAudio.Audio;

/// <summary>
/// Núcleo de áudio compartilhado (Fase 6, D1 a D3): um só por app. Assume o que a janela criava sozinha (SessionTracker,
/// DeviceMonitor, AudioSessionMonitor e AppRoutingService, com as mesmas ligações e a mesma ordem de criação e de
/// Dispose). Funciona por contagem de uso: o primeiro <see cref="Acquire"/> cria e inicia tudo, o último
/// <see cref="Release"/> solta tudo. A janela é um dono e cada celular conectado é outro. Todos os comandos passam por
/// aqui e avisam todos os consumidores. Só na thread da tela.
/// </summary>
internal sealed class AudioHub
{
    private readonly HashSet<string> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _preferred = new(StringComparer.Ordinal);
    private readonly DispatcherQueue _queue;
    private SessionTracker? _tracker;
    private DeviceMonitor? _deviceMonitor;
    private AudioSessionMonitor? _monitor;
    private AppRoutingService? _routing;
    private MediaMonitor? _mediaMonitor;
    private readonly AumidResolver _aumids = new(); // vive o app inteiro (caches de AUMID e .exe)
    private IReadOnlyList<MediaInfo> _media = Array.Empty<MediaInfo>();
    private bool _masterAvailable;
    private float _masterVolume;
    private bool _masterMuted;

    public AudioHub(DispatcherQueue queue)
    {
        _queue = queue;
    }

    /// <summary>Há algum dono (janela ou celular) e portanto os monitores existem.</summary>
    public bool IsActive => _owners.Count > 0;

    public int OwnerCount => _owners.Count;

    /// <summary>O primeiro dono chegou, antes de criar os monitores (o App desliga o modo de eficiência).</summary>
    public event Action? BecameActive;

    /// <summary>O último dono saiu, depois de soltar tudo (o App liga o modo de eficiência e agenda o trim).</summary>
    public event Action? BecameIdle;

    /// <summary>O volume geral mudou (de fora, ou por um comando do hub): disponível, volume de 0 a 1, mudo.</summary>
    public event Action<bool, float, bool>? MasterChanged;

    /// <summary>A escolha de saída de um app mudou por um comando do hub; recebe a chave do grupo.</summary>
    public event Action<string>? RouteChanged;

    /// <summary>Sessões agrupadas por app. Só vale enquanto houver dono (a instância muda a cada ativação).</summary>
    public SessionTracker Sessions => _tracker ?? throw new InvalidOperationException("hub inativo");

    /// <summary>Lista de saídas e saída padrão. Só vale enquanto houver dono.</summary>
    public DeviceTracker Devices => _deviceMonitor?.Tracker ?? throw new InvalidOperationException("hub inativo");

    /// <summary>Serviço da saída por app; nulo se o Windows não oferecer a interface.</summary>
    public AppRoutingService? Routing => _routing;

    /// <summary>A lista de mídia (ou a ligação dela com as linhas) mudou (Fase 7, D2).</summary>
    public event Action? MediaChanged;

    /// <summary>Apps com mídia agora; <see cref="MediaInfo.Key"/> é a linha de som ligada, ou nulo (linha só de mídia). Vale enquanto houver dono.</summary>
    public IReadOnlyList<MediaInfo> Media => _media;

    public bool MasterAvailable => _masterAvailable;
    public float MasterVolume => _masterVolume;
    public bool MasterMuted => _masterMuted;

    // ---- Contagem de uso (D1) ----

    public void Acquire(string owner)
    {
        if (!_owners.Add(owner)) return;
        if (_owners.Count > 1) return;
        BecameActive?.Invoke();
        Create();
    }

    public void Release(string owner)
    {
        if (!_owners.Remove(owner)) return;
        if (_owners.Count > 0) return;
        Destroy();
        BecameIdle?.Invoke();
    }

    /// <summary>Cria na mesma ordem que a janela usava: saídas, sessões e saída por app; depois liga e inicia.</summary>
    private void Create()
    {
        _tracker = new SessionTracker();
        _deviceMonitor = new DeviceMonitor(_queue);
        _monitor = new AudioSessionMonitor(_queue, _tracker);
        _routing = AppRoutingService.TryCreate(); // Fase 4, D5
        _preferred.Clear();
        _masterAvailable = false;
        _masterVolume = 0f;
        _masterMuted = false;

        // O hub assina antes dos outros consumidores: a escolha de saída de um app já está lida quando a linha aparece (D3).
        _tracker.Changed += OnGroupChanged;
        _deviceMonitor.MasterChanged += OnMasterChanged;
        try { _deviceMonitor.Start(); }
        catch (Exception ex) { Log.Write($"ERRO COM ao listar saídas: {ex}"); }
        _deviceMonitor.DeviceAdded += _monitor.AddDevice;
        _deviceMonitor.DeviceRemoved += _monitor.RemoveDevice;
        try { _monitor.Start(_deviceMonitor.Devices); }
        catch (Exception ex) { Log.Write($"ERRO COM ao listar sessões: {ex}"); }
        // Fase 7, D1: a mídia nasce junto com os outros monitores e some com eles.
        _media = Array.Empty<MediaInfo>();
        _mediaMonitor = new MediaMonitor(_queue);
        _mediaMonitor.Changed += OnMediaRead;
        _mediaMonitor.Start();
        Log.Write($"audio: monitores criados (donos={_owners.Count})");
    }

    /// <summary>Solta tudo na ordem da janela: assinaturas, saída por app, sessões e, por último, o DeviceMonitor (dono do enumerador).</summary>
    private void Destroy()
    {
        if (_tracker is not null) _tracker.Changed -= OnGroupChanged;
        if (_deviceMonitor is not null)
        {
            _deviceMonitor.MasterChanged -= OnMasterChanged;
            if (_monitor is not null)
            {
                _deviceMonitor.DeviceAdded -= _monitor.AddDevice;
                _deviceMonitor.DeviceRemoved -= _monitor.RemoveDevice;
            }
        }
        if (_mediaMonitor is not null) _mediaMonitor.Changed -= OnMediaRead;
        _mediaMonitor?.Dispose();
        _mediaMonitor = null;
        _aumids.ClearPids();
        _media = Array.Empty<MediaInfo>();
        _routing?.Dispose();
        _routing = null;
        _monitor?.Dispose();
        _monitor = null;
        _deviceMonitor?.Dispose();
        _deviceMonitor = null;
        _tracker = null;
        _preferred.Clear();
        Log.Write("audio: monitores liberados");
    }

    /// <summary>Solta todos os donos de uma vez (ao sair do app).</summary>
    public void ReleaseAll()
    {
        if (_owners.Count == 0) return;
        _owners.Clear();
        Destroy();
        BecameIdle?.Invoke();
    }

    // ---- Saída por app (D3) ----

    /// <summary>Escolha de saída do app (id do MMDevice); nulo se segue a saída padrão ou se ainda não foi lida.</summary>
    public string? PreferredOf(string key) => _preferred.TryGetValue(key, out var id) ? id : null;

    /// <summary>Se a escolha do app já foi lida do Windows.</summary>
    public bool IsRouteLoaded(string key) => _preferred.ContainsKey(key);

    /// <summary>
    /// Lê a escolha do app quando ele tem processo (Fase 4, D6). Devolve verdadeiro se já está lida. Sem processo ainda,
    /// tenta de novo quando a linha mudar.
    /// </summary>
    public bool TryLoadRoute(string key, bool isSystem)
    {
        if (_routing is null || _tracker is null || isSystem) return false;
        if (_preferred.ContainsKey(key)) return true;
        var pids = _tracker.ProcessIds(key);
        if (pids.Count == 0) return false;
        try { _preferred[key] = _routing.GetPreferred(pids); }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao ler a saida de {NameOf(key)}: {ex.Message}");
            _preferred[key] = null;
        }
        return true;
    }

    private void OnGroupChanged(GroupChange change)
    {
        switch (change.Kind)
        {
            case GroupChangeKind.Added:
                TryLoadRoute(change.Group.Key, change.Group.IsSystem);
                RecomputeMedia(); // uma linha nova pode ficar com a mídia de uma linha só de mídia
                break;
            case GroupChangeKind.Changed:
                TryLoadRoute(change.Group.Key, change.Group.IsSystem);
                break;
            case GroupChangeKind.Removed:
                _preferred.Remove(change.Group.Key);
                RecomputeMedia(); // a mídia que ficou sem linha vira linha só de mídia
                break;
        }
    }

    // ---- Mídia (Fase 7) ----

    private void OnMediaRead() => RecomputeMedia();

    /// <summary>
    /// Liga cada mídia lida a uma linha de som (D3) e avisa se a lista ou alguma ligação mudou. Sem mídia, não toca nas
    /// sessões. Só na thread da tela.
    /// </summary>
    private void RecomputeMedia()
    {
        if (_mediaMonitor is null || _tracker is null) return;
        var read = _mediaMonitor.Items;
        List<MediaInfo> next;
        if (read.Count == 0)
        {
            next = new List<MediaInfo>();
        }
        else
        {
            var candidates = read.Select(m => new MediaCandidate(m.Aumid, m.IsPlaying, _aumids.ShellResolve(m.Aumid).ExePath)).ToList();
            var groups = new List<GroupCandidate>();
            foreach (var g in _tracker.Groups)
            {
                if (g.IsSystem) continue;
                var aumids = _tracker.ProcessIds(g.Key)
                    .Select(_aumids.ProcessAumid).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                groups.Add(new GroupCandidate(g.Key, g.ExePath, aumids));
            }
            var map = MediaMatcher.Match(candidates, groups);
            next = read.Select(m => map.TryGetValue(m.Aumid, out var key) ? m with { Key = key } : m).ToList();
        }
        if (next.SequenceEqual(_media)) return;
        _media = next;
        MediaChanged?.Invoke();
    }

    /// <summary>Nome do app para a linha só de mídia: o da pasta de apps ou o do .exe sem extensão.</summary>
    public string MediaAppName(string aumid) => MediaMatcher.NameFor(aumid, _aumids.ShellResolve(aumid).Name);

    /// <summary>.exe do app para o ícone da linha só de mídia (pasta de apps; nos apps da Loja, o processo com o mesmo AUMID).</summary>
    public string? MediaExePath(string aumid)
    {
        var path = _aumids.ShellResolve(aumid).ExePath;
        return !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path) ? path : _aumids.StoreExePath(aumid);
    }

    /// <summary>Faixa anterior no app com esse AUMID (D5).</summary>
    public void MediaPrevious(string aumid) => _mediaMonitor?.Execute(aumid, MediaAction.Previous);

    /// <summary>Play ou pausa no app com esse AUMID (D5).</summary>
    public void MediaPlayPause(string aumid) => _mediaMonitor?.Execute(aumid, MediaAction.PlayPause);

    /// <summary>Próxima faixa no app com esse AUMID (D5).</summary>
    public void MediaNext(string aumid) => _mediaMonitor?.Execute(aumid, MediaAction.Next);

    // ---- Comandos (D2): gravam e avisam todos os consumidores ----

    /// <summary>Volume do app (0 a 1). O aviso vem pelo SessionTracker.</summary>
    public void SetVolume(string key, float volume) => _monitor?.SetVolume(key, volume);

    /// <summary>Mudo do app. O aviso vem pelo SessionTracker.</summary>
    public void SetMute(string key, bool muted) => _monitor?.SetMute(key, muted);

    /// <summary>Volume geral (0 a 1) da saída padrão; depois de gravar, avisa os consumidores.</summary>
    public void SetMasterVolume(float volume)
    {
        if (_deviceMonitor is null || !_masterAvailable) return;
        volume = Math.Clamp(volume, 0f, 1f);
        _deviceMonitor.SetMasterVolume(volume);
        _masterVolume = volume;
        MasterChanged?.Invoke(true, _masterVolume, _masterMuted);
    }

    /// <summary>Mudo geral da saída padrão; depois de gravar, avisa os consumidores.</summary>
    public void SetMasterMute(bool muted)
    {
        if (_deviceMonitor is null || !_masterAvailable) return;
        _deviceMonitor.SetMasterMute(muted);
        _masterMuted = muted;
        MasterChanged?.Invoke(true, _masterVolume, _masterMuted);
    }

    /// <summary>Troca a saída padrão. O aviso continua vindo do evento do Windows (Fase 3, D10).</summary>
    public void SetDefaultDevice(string id) => _deviceMonitor?.SetDefaultDevice(id);

    /// <summary>
    /// Clique num botão de saída de um app (Fase 4, D7): a saída clicada que já é a efetiva não faz nada, a padrão apaga a
    /// escolha e qualquer outra vira a escolha.
    /// </summary>
    public void ClickRoute(string key, string clickedId)
    {
        if (_routing is null || _tracker is null || _deviceMonitor is null) return;
        if (!_preferred.TryGetValue(key, out var preferred)) return;
        var action = AppRouting.OnClick(clickedId, preferred, Devices.DefaultId);
        switch (action.Kind)
        {
            case RouteActionKind.Clear: SetRoute(key, null); break;
            case RouteActionKind.Set: SetRoute(key, action.DeviceId); break;
        }
    }

    /// <summary>Grava (ou apaga, com nulo) a escolha de saída do app, relê para confirmar e avisa com <see cref="RouteChanged"/>.</summary>
    public void SetRoute(string key, string? deviceId)
    {
        if (_routing is null || _tracker is null) return;
        var name = NameOf(key);
        try
        {
            var pids = _tracker.ProcessIds(key);
            _routing.SetPreferred(pids, deviceId);
            if (deviceId is null) Log.Write($"saida de {name} volta a seguir a padrao");
            else Log.Write($"saida de {name} trocada para {ShortNameOf(deviceId)}");
            _preferred[key] = _routing.GetPreferred(pids);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO ao trocar a saida de {name}: {ex.Message}");
        }
        RouteChanged?.Invoke(key);
    }

    // ---- Auxiliares ----

    private void OnMasterChanged(bool available, float volume, bool muted)
    {
        _masterAvailable = available;
        _masterVolume = volume;
        _masterMuted = muted;
        MasterChanged?.Invoke(available, volume, muted);
    }

    private string NameOf(string key)
    {
        if (_tracker is null) return key;
        foreach (var g in _tracker.Groups)
            if (g.Key == key) return g.Name;
        return key;
    }

    private string ShortNameOf(string id)
    {
        foreach (var d in Devices.Devices)
            if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d.ShortName;
        return id;
    }
}
