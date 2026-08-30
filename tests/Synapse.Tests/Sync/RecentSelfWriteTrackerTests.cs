using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Sync;

namespace Synapse.Tests.Sync;

public class RecentSelfWriteTrackerTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly RecentSelfWriteTracker _tracker;

    public RecentSelfWriteTrackerTests()
    {
        _tracker = new RecentSelfWriteTracker(TimeSpan.FromSeconds(3), _timeProvider);
    }

    [Fact]
    public void ArquivoNaoMarcado_RetornaFalso()
    {
        _tracker.WasRecentlyWrittenByUs("notas/qualquer.md").ShouldBeFalse();
    }

    [Fact]
    public void ArquivoMarcado_DentroDaJanela_RetornaVerdadeiro()
    {
        _tracker.MarkWritten("notas/exemplo.md");

        _tracker.WasRecentlyWrittenByUs("notas/exemplo.md").ShouldBeTrue();
    }

    [Fact]
    public void ArquivoMarcado_AposExpirarJanela_RetornaFalso()
    {
        _tracker.MarkWritten("notas/exemplo.md");

        _timeProvider.Advance(TimeSpan.FromSeconds(3.1));

        _tracker.WasRecentlyWrittenByUs("notas/exemplo.md").ShouldBeFalse();
    }

    [Fact]
    public void NormalizacaoDeCaminho_IgnoraBarrasEIniciaisEMaiusculas()
    {
        _tracker.MarkWritten("pasta\\subpasta/nota.md");

        _tracker.WasRecentlyWrittenByUs("pasta/subpasta/nota.md").ShouldBeTrue();
        _tracker.WasRecentlyWrittenByUs("/pasta/subpasta/nota.md").ShouldBeTrue();
        _tracker.WasRecentlyWrittenByUs("PASTA\\SUBPASTA\\NOTA.MD").ShouldBeTrue();
    }

    [Fact]
    public void MultiplasConsultasDentroDaJanela_MantemRetornoVerdadeiro()
    {
        _tracker.MarkWritten("notas/exemplo.md");

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        _tracker.WasRecentlyWrittenByUs("notas/exemplo.md").ShouldBeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(1));
        _tracker.WasRecentlyWrittenByUs("notas/exemplo.md").ShouldBeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(1.5)); // total 3.5s > 3s
        _tracker.WasRecentlyWrittenByUs("notas/exemplo.md").ShouldBeFalse();
    }

    [Fact]
    public void CaminhoVazioOuNulo_RetornaFalsoSemExcecao()
    {
        _tracker.MarkWritten("");
        _tracker.WasRecentlyWrittenByUs("").ShouldBeFalse();
        _tracker.WasRecentlyWrittenByUs(null!).ShouldBeFalse();
    }
}
