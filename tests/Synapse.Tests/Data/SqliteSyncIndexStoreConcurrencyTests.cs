using Shouldly;
using Synapse.Core.Ports;
using Synapse.Data;
using Xunit;

namespace Synapse.Tests.Data;

/// <summary>
/// O Worker do Host roda quatro Task.Run em paralelo, e tres delas tocam o MESMO
/// ISyncIndexStore singleton (fila de eventos, poller remoto, reconciliacao). O comentario
/// da classe afirma escritor unico. Este teste mede se a conexao compartilhada aguenta.
/// </summary>
public class SqliteSyncIndexStoreConcurrencyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"synapse-conc-{Guid.NewGuid():N}.db");
    private readonly SqliteSyncIndexStore _store;

    public SqliteSyncIndexStoreConcurrencyTests() => _store = SqliteSyncIndexStore.ForFile(_dbPath);

    public void Dispose()
    {
        _store.Dispose();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { }
        }
    }

    private static SyncedFileRecord Registro(string path) => new(
        Id: 0, LocalPath: path, CloudFileId: "id", ContentHash: "h",
        LocalMtime: DateTimeOffset.UtcNow, CloudModifiedTime: DateTimeOffset.UtcNow,
        LastSyncedAt: DateTimeOffset.UtcNow, Status: SyncStatus.Synced);

    [Fact]
    public async Task TresLacosSimultaneos_ComoNoWorker()
    {
        // 120s, nao 30s: este limite e rede contra DEADLOCK, nao asserção de desempenho. Com 30s o
        // teste falhava de forma consistente no runner do CI (mais lento que a maquina local), o
        // proprio token cancelava as operacoes no meio e o TaskCanceledException resultante era
        // reportado como "conexao compartilhada quebrou" - acusando o produto por lentidao da
        // maquina. Um travamento real continua sendo pego, so que sem falso positivo.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = cts.Token;

        var escritor = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                await _store.UpsertAsync(Registro($"notas/a{i}.md"), ct);
                await _store.EnqueueAsync(new SyncQueueItem(0, $"notas/a{i}.md", SyncEventType.Modified, DateTimeOffset.UtcNow, 0, null), ct);
            }
        }, ct);

        var fila = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                await _store.PeekNextAsync(ct);
            }
        }, ct);

        var leitor = Task.Run(async () =>
        {
            for (var i = 0; i < 300; i++)
            {
                await _store.FindByLocalPathAsync($"notas/a{i}.md", ct);
            }
        }, ct);

        var erro = await Record.ExceptionAsync(() => Task.WhenAll(escritor, fila, leitor));

        // Distingue as duas causas: se foi o NOSSO token que estourou, o problema e travamento
        // (ou maquina lenta demais), nao a conexao compartilhada. Reportar as duas com a mesma
        // mensagem foi o que mascarou a causa real da falha no CI durante horas.
        if (erro is not null && cts.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException(
                "As tres tarefas nao terminaram em 120s - possivel deadlock na conexao compartilhada " +
                $"(ou maquina excepcionalmente lenta). Excecao: {erro.GetType().Name}: {erro.Message}");
        }

        erro.ShouldBeNull($"conexao compartilhada quebrou: {erro?.GetType().Name}: {erro?.Message}");
    }
}
