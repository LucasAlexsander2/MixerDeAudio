using System;
using System.Collections.Generic;
using System.Threading;

namespace MixerDeAudio.Pc;

/// <summary>
/// Aplica o brilho em segundo plano (Fase 12, D6 e D8). Um único fio executa um comando DDC por vez; de cada tela guarda só o
/// último valor pedido (os intermediários são descartados). A leitura e a escrita são funções injetadas (a real usa DDC; o
/// teste, uma ação falsa). <see cref="Changed"/> e <see cref="Failed"/> são chamados no fio do worker.
/// </summary>
public sealed class BrightnessWorker : IDisposable
{
    private readonly Func<IReadOnlyList<int?>> _read;
    private readonly Func<int, int, bool> _write;
    private readonly object _lock = new();
    private readonly SortedDictionary<int, int> _pending = new();
    private readonly Thread _thread;
    private IReadOnlyList<int?> _values = Array.Empty<int?>();
    private bool _readPending, _busy, _disposed;

    public BrightnessWorker(Func<IReadOnlyList<int?>> read, Func<int, int, bool> write)
    {
        _read = read;
        _write = write;
        _thread = new Thread(Loop) { IsBackground = true, Name = "BrilhoDDC" };
        _thread.Start();
    }

    /// <summary>O valor guardado de cada tela (0 a 100; nulo = sem suporte). Lista vazia até a primeira leitura.</summary>
    public IReadOnlyList<int?> Values
    {
        get { lock (_lock) return _values; }
    }

    /// <summary>Os valores guardados mudaram (leitura ou escrita concluída). Chamado no fio do worker.</summary>
    public event Action? Changed;

    /// <summary>A escrita falhou na tela dada (D12). Chamado no fio do worker.</summary>
    public event Action<int>? Failed;

    /// <summary>Pede uma leitura de todas as telas.</summary>
    public void Refresh()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _readPending = true;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    /// Pede o brilho da tela (cortado em 0 a 100). Falso se a tela não existe ou não tem suporte (D10); um pedido novo
    /// substitui o que ainda não começou.
    /// </summary>
    public bool Request(int tela, int pct)
    {
        lock (_lock)
        {
            if (_disposed || tela < 0 || tela >= _values.Count || _values[tela] is null) return false;
            _pending[tela] = BrightnessRules.ClampPercent(pct);
            Monitor.PulseAll(_lock);
            return true;
        }
    }

    /// <summary>Espera a fila esvaziar e o worker ficar parado (para os testes). Falso se passou do tempo.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        lock (_lock)
        {
            while (_readPending || _busy || _pending.Count > 0)
            {
                var left = end - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_lock, left);
            }
            return true;
        }
    }

    private void Loop()
    {
        while (true)
        {
            bool doRead;
            int tela = -1, pct = 0;
            lock (_lock)
            {
                while (!_disposed && !_readPending && _pending.Count == 0) Monitor.Wait(_lock);
                if (_disposed) return;
                doRead = _readPending;
                if (doRead)
                {
                    _readPending = false;
                }
                else
                {
                    foreach (var kv in _pending) { tela = kv.Key; pct = kv.Value; break; }
                    _pending.Remove(tela);
                }
                _busy = true;
            }

            var changed = false;
            try
            {
                if (doRead)
                {
                    var list = _read();
                    lock (_lock) _values = list;
                    changed = true;
                }
                else if (_write(tela, pct))
                {
                    lock (_lock)
                    {
                        if (tela < _values.Count)
                        {
                            var copy = new List<int?>(_values);
                            copy[tela] = pct;
                            _values = copy;
                        }
                    }
                    changed = true;
                }
                else
                {
                    RaiseFailed(tela);
                }
            }
            catch
            {
                if (!doRead) RaiseFailed(tela);
            }
            try { if (changed) Changed?.Invoke(); }
            catch { /* o aviso não pode derrubar o worker */ }

            lock (_lock)
            {
                _busy = false;
                Monitor.PulseAll(_lock);
            }
        }
    }

    private void RaiseFailed(int tela)
    {
        try { Failed?.Invoke(tela); }
        catch { /* idem */ }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            Monitor.PulseAll(_lock);
        }
    }
}
