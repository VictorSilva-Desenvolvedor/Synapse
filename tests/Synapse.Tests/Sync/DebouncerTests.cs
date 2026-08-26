using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync;

namespace Synapse.Tests.Sync;

// TC-10 (Plano de Testes): debounce agrupa multiplos eventos rapidos no mesmo arquivo em um so.
// Usa FakeTimeProvider para avancar o tempo manualmente, sem depender de tempo real de parede.
public class DebouncerTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<VaultChangeEvent> _publicados = [];
    private readonly Debouncer _debouncer;

    public DebouncerTests()
    {
        _debouncer = new Debouncer(_publicados.Add, TimeSpan.FromMilliseconds(2000), _timeProvider);
    }

    [Fact]
    public void RajadaDeEventosNoMesmoCaminho_PublicaSoUmaVez()
    {
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Modified));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Modified));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Modified));

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1999));
        _publicados.ShouldBeEmpty();

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        _publicados.Count.ShouldBe(1);
        _publicados[0].RelativePath.ShouldBe("nota.md");
    }

    [Fact]
    public void EventoUnico_PublicaAposAJanelaDeSilencio()
    {
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Created));

        _timeProvider.Advance(TimeSpan.FromMilliseconds(2000));

        _publicados.Count.ShouldBe(1);
        _publicados[0].EventType.ShouldBe(SyncEventType.Created);
    }

    [Fact]
    public void CaminhosDiferentes_SaoDebounceadosIndependentemente()
    {
        _debouncer.OnRawEvent(new VaultChangeEvent("a.md", SyncEventType.Modified));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        _debouncer.OnRawEvent(new VaultChangeEvent("b.md", SyncEventType.Modified));

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        _publicados.Count.ShouldBe(1);
        _publicados[0].RelativePath.ShouldBe("a.md");

        _timeProvider.Advance(TimeSpan.FromMilliseconds(1000));
        _publicados.Count.ShouldBe(2);
        _publicados[1].RelativePath.ShouldBe("b.md");
    }

    [Fact]
    public void NovaRajadaAposJanelaAnterior_PublicaDeNovo()
    {
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Modified));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(2000));
        _publicados.Count.ShouldBe(1);

        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Modified));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(2000));

        _publicados.Count.ShouldBe(2);
    }

    [Fact]
    public void UltimoEventoDaRajadaEQueEPublicado()
    {
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Created));
        _timeProvider.Advance(TimeSpan.FromMilliseconds(500));
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Deleted));

        _timeProvider.Advance(TimeSpan.FromMilliseconds(2000));

        _publicados.Count.ShouldBe(1);
        _publicados[0].EventType.ShouldBe(SyncEventType.Deleted);
    }

    [Fact]
    public void Dispose_CancelaTimersPendentesSemPublicar()
    {
        _debouncer.OnRawEvent(new VaultChangeEvent("nota.md", SyncEventType.Modified));

        _debouncer.Dispose();
        _timeProvider.Advance(TimeSpan.FromMilliseconds(5000));

        _publicados.ShouldBeEmpty();
    }
}
