using Shouldly;
using Synapse.Core.Ports;
using Synapse.Data;

namespace Synapse.Tests.Data;

// Nivel de integracao (Plano de Testes secao 4): SQLite real em arquivo temporario, nao um fake em
// memoria - e o proprio banco do produto, nao infraestrutura externa.
public class SqliteSyncIndexStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteSyncIndexStore _store;

    public SqliteSyncIndexStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"synapse-tests-{Guid.NewGuid():N}.db");
        _store = SqliteSyncIndexStore.ForFile(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static SyncedFileRecord NovoRegistro(string localPath = "notas/exemplo.md") => new(
        Id: 0,
        LocalPath: localPath,
        CloudFileId: "drive-id-1",
        ContentHash: "hash-abc",
        LocalMtime: DateTimeOffset.UtcNow,
        CloudModifiedTime: DateTimeOffset.UtcNow,
        LastSyncedAt: DateTimeOffset.UtcNow,
        Status: SyncStatus.Synced);

    [Fact]
    public async Task UpsertEFind_FazRoundTripDosCampos()
    {
        var registro = NovoRegistro();

        await _store.UpsertAsync(registro, CancellationToken.None);
        var encontrado = await _store.FindByLocalPathAsync(registro.LocalPath, CancellationToken.None);

        encontrado.ShouldNotBeNull();
        encontrado.LocalPath.ShouldBe(registro.LocalPath);
        encontrado.CloudFileId.ShouldBe(registro.CloudFileId);
        encontrado.ContentHash.ShouldBe(registro.ContentHash);
        encontrado.Status.ShouldBe(SyncStatus.Synced);
        encontrado.LocalMtime.ToUnixTimeMilliseconds().ShouldBe(registro.LocalMtime.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task FindByLocalPath_QuandoNaoExiste_RetornaNulo()
    {
        var encontrado = await _store.FindByLocalPathAsync("nao/existe.md", CancellationToken.None);

        encontrado.ShouldBeNull();
    }

    [Fact]
    public async Task FindByCloudFileId_EncontraRegistroCorrespondente()
    {
        var registro = NovoRegistro() with { CloudFileId = "drive-id-especial" };
        await _store.UpsertAsync(registro, CancellationToken.None);

        var encontrado = await _store.FindByCloudFileIdAsync("drive-id-especial", CancellationToken.None);

        encontrado.ShouldNotBeNull();
        encontrado.LocalPath.ShouldBe(registro.LocalPath);
    }

    [Fact]
    public async Task UpsertDuasVezesComMesmoLocalPath_AtualizaEmVezDeDuplicar()
    {
        var registro = NovoRegistro();
        await _store.UpsertAsync(registro, CancellationToken.None);
        var primeiraLeitura = await _store.FindByLocalPathAsync(registro.LocalPath, CancellationToken.None);

        var atualizado = registro with { ContentHash = "hash-novo", Status = SyncStatus.PendingUpload };
        await _store.UpsertAsync(atualizado, CancellationToken.None);
        var segundaLeitura = await _store.FindByLocalPathAsync(registro.LocalPath, CancellationToken.None);

        segundaLeitura.ShouldNotBeNull();
        segundaLeitura.ContentHash.ShouldBe("hash-novo");
        segundaLeitura.Status.ShouldBe(SyncStatus.PendingUpload);
        segundaLeitura.Id.ShouldBe(primeiraLeitura!.Id); // mesma linha, nao duplicou
    }

    [Fact]
    public async Task RemoveAsync_ApagaORegistro()
    {
        var registro = NovoRegistro();
        await _store.UpsertAsync(registro, CancellationToken.None);

        await _store.RemoveAsync(registro.LocalPath, CancellationToken.None);
        var encontrado = await _store.FindByLocalPathAsync(registro.LocalPath, CancellationToken.None);

        encontrado.ShouldBeNull();
    }

    // Contrato de API - Synapse.md 2.2: PeekNext NAO remove o item da fila.
    [Fact]
    public async Task PeekNext_NaoRemoveOItemDaFila()
    {
        var item = new SyncQueueItem(0, "notas/a.md", SyncEventType.Modified, DateTimeOffset.UtcNow, 0, null);
        await _store.EnqueueAsync(item, CancellationToken.None);

        var primeiroPeek = await _store.PeekNextAsync(CancellationToken.None);
        var segundoPeek = await _store.PeekNextAsync(CancellationToken.None);

        primeiroPeek.ShouldNotBeNull();
        segundoPeek.ShouldNotBeNull();
        segundoPeek.Id.ShouldBe(primeiroPeek.Id);
    }

    [Fact]
    public async Task PeekNext_RetornaOItemMaisAntigoPrimeiro()
    {
        await _store.EnqueueAsync(new SyncQueueItem(0, "primeiro.md", SyncEventType.Created, DateTimeOffset.UtcNow, 0, null), CancellationToken.None);
        await _store.EnqueueAsync(new SyncQueueItem(0, "segundo.md", SyncEventType.Created, DateTimeOffset.UtcNow, 0, null), CancellationToken.None);

        var proximo = await _store.PeekNextAsync(CancellationToken.None);

        proximo.ShouldNotBeNull();
        proximo.FilePath.ShouldBe("primeiro.md");
    }

    [Fact]
    public async Task MarkDone_RemoveOItemDaFila()
    {
        await _store.EnqueueAsync(new SyncQueueItem(0, "notas/a.md", SyncEventType.Modified, DateTimeOffset.UtcNow, 0, null), CancellationToken.None);
        var item = await _store.PeekNextAsync(CancellationToken.None);

        await _store.MarkDoneAsync(item!.Id, CancellationToken.None);
        var proximo = await _store.PeekNextAsync(CancellationToken.None);

        proximo.ShouldBeNull();
    }

    [Fact]
    public async Task MarkFailed_IncrementaTentativasEMantemOItemNaFila()
    {
        await _store.EnqueueAsync(new SyncQueueItem(0, "notas/a.md", SyncEventType.Modified, DateTimeOffset.UtcNow, 0, null), CancellationToken.None);
        var item = await _store.PeekNextAsync(CancellationToken.None);

        await _store.MarkFailedAsync(item!.Id, "erro de rede", CancellationToken.None);
        var depois = await _store.PeekNextAsync(CancellationToken.None);

        depois.ShouldNotBeNull();
        depois.Attempts.ShouldBe(1);
        depois.LastError.ShouldBe("erro de rede");
    }

    [Fact]
    public async Task ChangesPageToken_ComecaNuloEPersisteAposSalvar()
    {
        var inicial = await _store.GetChangesPageTokenAsync(CancellationToken.None);
        inicial.ShouldBeNull();

        await _store.SaveChangesPageTokenAsync("token-1", CancellationToken.None);
        var depoisDeSalvar = await _store.GetChangesPageTokenAsync(CancellationToken.None);

        depoisDeSalvar.ShouldBe("token-1");
    }

    [Fact]
    public async Task ChangesPageToken_SalvarNovamenteSubstituiOValorAnterior()
    {
        await _store.SaveChangesPageTokenAsync("token-1", CancellationToken.None);
        await _store.SaveChangesPageTokenAsync("token-2", CancellationToken.None);

        var valor = await _store.GetChangesPageTokenAsync(CancellationToken.None);

        valor.ShouldBe("token-2");
    }

    [Fact]
    public async Task RecordConflict_NaoLancaExcecao()
    {
        var conflito = new ConflictRecord("notas/a.md", "_conflitos/notas/a.md/local-1.md", "_conflitos/notas/a.md/remoto-1.md", DateTimeOffset.UtcNow);

        await Should.NotThrowAsync(() => _store.RecordConflictAsync(conflito, CancellationToken.None));
    }

    // TC-04 (Plano de Testes): reinicio do processo com itens pendentes na fila - nenhum item e perdido.
    [Fact]
    public async Task NovaInstanciaApontandoParaOMesmoArquivo_VeOsItensPersistidos()
    {
        await _store.EnqueueAsync(new SyncQueueItem(0, "notas/sobrevive.md", SyncEventType.Modified, DateTimeOffset.UtcNow, 0, null), CancellationToken.None);

        using var novaInstancia = SqliteSyncIndexStore.ForFile(_dbPath);
        var item = await novaInstancia.PeekNextAsync(CancellationToken.None);

        item.ShouldNotBeNull();
        item.FilePath.ShouldBe("notas/sobrevive.md");
    }
}
