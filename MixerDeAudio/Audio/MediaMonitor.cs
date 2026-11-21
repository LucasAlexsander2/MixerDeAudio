using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using MixerDeAudio.Services;
using Windows.Media.Control;

namespace MixerDeAudio.Audio;

/// <summary>
/// Observa o controle de mídia do Windows (GSMTC, Fase 7, D1): a lista de apps com mídia, o título, o artista, o estado e os
/// botões que cada um oferece. Existe só enquanto o <see cref="AudioHub"/> tem dono. Os eventos chegam em threads
/// quaisquer: os tratadores só marcam o app e a thread da tela relê, coalescido em 100 ms. Posição e tempo da faixa não
/// são lidos. Não há <c>Dispose</c> nos objetos WinRT: o <see cref="Dispose"/> só cancela as assinaturas e solta as
/// referências. Só na thread da tela (menos os tratadores de evento).
/// </summary>
internal sealed class MediaMonitor : IDisposable
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);

    private sealed class Entry
    {
        public required string Aumid;
        public required GlobalSystemMediaTransportControlsSession Session;
        public required Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> OnPlayback;
        public required Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> OnProperties;
        public MediaInfo Info = null!;
        public bool Toggle;
    }

    private readonly DispatcherQueue _queue;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();
    private readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs>? _onSessions;
    private DispatcherQueueTimer? _timer;
    private bool _resync;
    private bool _refreshing;
    private volatile bool _disposed;
    private List<MediaInfo> _items = new();

    public MediaMonitor(DispatcherQueue queue)
    {
        _queue = queue;
    }

    /// <summary>A lista mudou (já na thread da tela). Os itens vêm sem <see cref="MediaInfo.Key"/>.</summary>
    public event Action? Changed;

    /// <summary>Apps com mídia agora, na ordem do Windows, sem a ligação com as linhas de som.</summary>
    public IReadOnlyList<MediaInfo> Items => _items;

    /// <summary>Pede o gerenciador sem bloquear; a resposta só vale se o monitor continua vivo (mesma geração do hub).</summary>
    public async void Start()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO midia: sem controle de midia ({ex.Message})");
            return;
        }
        if (_disposed) return;
        _queue.TryEnqueue(() => OnManager(manager));
    }

    private void OnManager(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        if (_disposed) return;
        _manager = manager;
        _onSessions = (_, _) => Post(() => { _resync = true; Schedule(); });
        manager.SessionsChanged += _onSessions;
        _resync = true;
        Schedule();
    }

    private void Post(Action action)
    {
        if (_disposed) return;
        _queue.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { action(); }
            catch (Exception ex) { Log.Write($"ERRO midia em evento: {ex.Message}"); }
        });
    }

    private void Schedule()
    {
        if (_disposed) return;
        if (_timer is null)
        {
            _timer = _queue.CreateTimer();
            _timer.Interval = Coalesce;
            _timer.IsRepeating = false;
            _timer.Tick += OnTick;
        }
        if (!_timer.IsRunning) _timer.Start();
    }

    private async void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed) return;
        if (_refreshing)
        {
            Schedule();
            return;
        }
        _refreshing = true;
        try
        {
            if (_resync) { _resync = false; Resync(); }
            var batch = _dirty.ToList();
            _dirty.Clear();
            foreach (var aumid in batch)
            {
                if (!_entries.TryGetValue(aumid, out var entry)) continue;
                var info = await ReadAsync(entry);
                if (_disposed) return;
                if (!_entries.ContainsKey(aumid)) continue; // saiu durante a leitura
                entry.Info = info;
            }
            Publish();
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO midia ao reler: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
        if (!_disposed && (_dirty.Count > 0 || _resync)) Schedule();
    }

    /// <summary>Compara a lista de sessões do Windows com as que já estão assinadas: assina as novas e solta as que saíram.</summary>
    private void Resync()
    {
        if (_manager is null) return;
        var current = new List<GlobalSystemMediaTransportControlsSession>();
        try { current.AddRange(_manager.GetSessions()); }
        catch (Exception ex) { Log.Write($"ERRO midia ao listar: {ex.Message}"); return; }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var session in current)
        {
            string aumid;
            try { aumid = session.SourceAppUserModelId; }
            catch { continue; }
            if (string.IsNullOrEmpty(aumid) || !seen.Add(aumid)) continue;
            order.Add(aumid);
            if (_entries.ContainsKey(aumid)) continue;
            var entry = new Entry
            {
                Aumid = aumid,
                Session = session,
                OnPlayback = (_, _) => Post(() => Mark(aumid)),
                OnProperties = (_, _) => Post(() => Mark(aumid)),
            };
            entry.Info = new MediaInfo(aumid, string.Empty, string.Empty, MediaStatus.Other, false, false, false);
            session.PlaybackInfoChanged += entry.OnPlayback;
            session.MediaPropertiesChanged += entry.OnProperties;
            _entries[aumid] = entry;
            _dirty.Add(aumid);
        }
        foreach (var aumid in _entries.Keys.Where(a => !seen.Contains(a)).ToList())
        {
            Unsubscribe(_entries[aumid]);
            _entries.Remove(aumid);
            _dirty.Remove(aumid);
        }
        _order.Clear();
        _order.AddRange(order);
    }

    private void Mark(string aumid)
    {
        if (!_entries.ContainsKey(aumid)) return;
        _dirty.Add(aumid);
        Schedule();
    }

    private static async Task<MediaInfo> ReadAsync(Entry entry)
    {
        var session = entry.Session;
        var status = MediaStatus.Other;
        bool toggle = false, play = false, pause = false, prev = false, next = false;
        try
        {
            var playback = session.GetPlaybackInfo();
            var c = playback.Controls;
            status = playback.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaStatus.Paused,
                _ => MediaStatus.Other,
            };
            toggle = c.IsPlayPauseToggleEnabled;
            play = c.IsPlayEnabled;
            pause = c.IsPauseEnabled;
            prev = c.IsPreviousEnabled;
            next = c.IsNextEnabled;
        }
        catch { /* sessão que acabou de sair: fica sem botões */ }

        string title = entry.Info.Title, artist = entry.Info.Artist;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = MediaInfo.Clean(props.Title);
            artist = MediaInfo.Clean(props.Artist);
        }
        catch { /* mantém o que já tinha */ }

        entry.Toggle = toggle;
        return new MediaInfo(entry.Aumid, title, artist, status, prev, next,
            MediaInfo.PlayPauseAvailable(toggle, play, pause, status));
    }

    private void Publish()
    {
        var list = _order.Where(_entries.ContainsKey).Select(a => _entries[a].Info).ToList();
        if (list.SequenceEqual(_items)) return;
        _items = list;
        Changed?.Invoke();
    }

    // ---- Comandos (D5): identificados pelo AUMID, não pela linha ----

    /// <summary>Anterior, play/pausa ou próxima no app. Resultado falso ou exceção vira uma linha no log.</summary>
    public async void Execute(string aumid, MediaAction action)
    {
        if (_disposed || !_entries.TryGetValue(aumid, out var entry)) return;
        var ok = false;
        try
        {
            var session = entry.Session;
            ok = action switch
            {
                MediaAction.Previous => await session.TrySkipPreviousAsync(),
                MediaAction.Next => await session.TrySkipNextAsync(),
                _ => entry.Toggle ? await session.TryTogglePlayPauseAsync()
                    : entry.Info.IsPlaying ? await session.TryPauseAsync() : await session.TryPlayAsync(),
            };
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO midia: {ex.Message}");
        }
        if (!ok) Log.Write($"midia: comando recusado ({NameOf(action)})");
    }

    private static string NameOf(MediaAction action) => action switch
    {
        MediaAction.Previous => "anterior",
        MediaAction.Next => "proxima",
        _ => "play/pausa",
    };

    private static void Unsubscribe(Entry entry)
    {
        try
        {
            entry.Session.PlaybackInfoChanged -= entry.OnPlayback;
            entry.Session.MediaPropertiesChanged -= entry.OnProperties;
        }
        catch (Exception ex)
        {
            Log.Write($"ERRO midia ao cancelar inscricao: {ex.Message}");
        }
    }

    /// <summary>Cancela todas as assinaturas e solta as referências.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        if (_manager is not null && _onSessions is not null)
        {
            try { _manager.SessionsChanged -= _onSessions; }
            catch (Exception ex) { Log.Write($"ERRO midia ao cancelar sessoes: {ex.Message}"); }
        }
        _manager = null;
        _onSessions = null;
        foreach (var entry in _entries.Values) Unsubscribe(entry);
        _entries.Clear();
        _order.Clear();
        _dirty.Clear();
        _items = new List<MediaInfo>();
    }
}
