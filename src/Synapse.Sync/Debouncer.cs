using Synapse.Core.Ports;

namespace Synapse.Sync;

/// <summary>
/// Agrupa eventos em rajada do mesmo caminho de arquivo em um só (RF-SYNC.1): um timer cancelável por
/// caminho, reiniciado a cada novo evento; só publica quando a janela de silêncio expira sem novo
/// evento. Usa TimeProvider (não Task.Delay/Timer reais) para ser testável com FakeTimeProvider (TC-10,
/// Plano de Testes) sem depender de tempo real de parede.
/// </summary>
public sealed class Debouncer : IDisposable
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(2000);

    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;
    private readonly Action<VaultChangeEvent> _onDebounced;
    private readonly Dictionary<string, (ITimer Timer, VaultChangeEvent Event)> _pending = [];
    private readonly object _gate = new();

    public Debouncer(Action<VaultChangeEvent> onDebounced, TimeSpan? window = null, TimeProvider? timeProvider = null)
    {
        _onDebounced = onDebounced;
        _window = window ?? DefaultWindow;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void OnRawEvent(VaultChangeEvent evt)
    {
        lock (_gate)
        {
            if (_pending.Remove(evt.RelativePath, out var existing))
                existing.Timer.Dispose();

            var timer = _timeProvider.CreateTimer(Fire, evt.RelativePath, _window, Timeout.InfiniteTimeSpan);
            _pending[evt.RelativePath] = (timer, evt);
        }
    }

    private void Fire(object? state)
    {
        var relativePath = (string)state!;
        VaultChangeEvent evt;

        lock (_gate)
        {
            // Pode ja ter sido removido/substituido entre o disparo do timer e a aquisicao do lock
            // (novo evento chegando durante o processamento) - contrato do RF-SYNC.1.
            if (!_pending.Remove(relativePath, out var entry))
                return;

            entry.Timer.Dispose();
            evt = entry.Event;
        }

        _onDebounced(evt);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (timer, _) in _pending.Values)
                timer.Dispose();

            _pending.Clear();
        }
    }
}
