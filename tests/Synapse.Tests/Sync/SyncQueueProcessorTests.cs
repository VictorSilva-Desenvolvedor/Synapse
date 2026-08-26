using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Conflict;
using Synapse.Core.Ports;
using Synapse.Sync;
using Synapse.Tests.TestDoubles;

namespace Synapse.Tests.Sync;

// Nivel "sistema simulado" (Plano de Testes secao 4): nuvem e disco simulados via FakeCloudProvider +
// InMemoryFileSystem + InMemorySyncIndexStore, com o ConflictResolver real (logica pura, sem motivo
// para dublar).
public class SyncQueueProcessorTests
{
    private const string VaultRoot = "/vault";
    private const string CacheRoot = "/cache";

    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly InMemorySyncIndexStore _indexStore = new();
    private readonly FakeCloudProvider _cloudProvider;
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly SyncQueueProcessor _processor;

    public SyncQueueProcessorTests()
    {
        _cloudProvider = new FakeCloudProvider(_fileSystem, _timeProvider);
        var options = new SyncQueueProcessorOptions(VaultRoot, "pasta-remota-raiz", CacheRoot, MaxAttempts: 3);
        _processor = new SyncQueueProcessor(_cloudProvider, _indexStore, new ConflictResolver(), _fileSystem, options, _timeProvider);
    }

    private static string VaultPath(string relativePath) => $"{VaultRoot}/{relativePath}";
    private static string CachePath(string relativePath) => $"{CacheRoot}/{relativePath}";

    [Fact]
    public async Task ArquivoNovoLocal_EhEnviadoEIndexado()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "conteudo novo", CancellationToken.None);

        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registro.ShouldNotBeNull();
        registro.Status.ShouldBe(SyncStatus.Synced);
        registro.CloudFileId.ShouldNotBeNull();

        var cacheado = await _fileSystem.ReadAllTextAsync(CachePath("nota.md"), CancellationToken.None);
        cacheado.ShouldBe("conteudo novo");
    }

    [Fact]
    public async Task ArquivoLocalModificado_AtualizaONuvemEOIndice()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "versao original", CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "versao editada", CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Modified), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registro.ShouldNotBeNull();
        registro.Status.ShouldBe(SyncStatus.Synced);

        var cacheado = await _fileSystem.ReadAllTextAsync(CachePath("nota.md"), CancellationToken.None);
        cacheado.ShouldBe("versao editada");
    }

    [Fact]
    public async Task SoMudouRemoto_BaixaEAtualizaOArquivoLocal()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "versao original", CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);
        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        _cloudProvider.SimularMudancaRemota(registro!.CloudFileId!, "versao vinda de outro dispositivo", _timeProvider.GetUtcNow());

        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Modified), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var conteudoLocal = await _fileSystem.ReadAllTextAsync(VaultPath("nota.md"), CancellationToken.None);
        conteudoLocal.ShouldBe("versao vinda de outro dispositivo");
    }

    [Fact]
    public async Task ArquivoDeletadoLocalmente_PropagaExclusaoERemoveDoIndice()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "conteudo", CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        await _fileSystem.DeleteAsync(VaultPath("nota.md"), CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Deleted), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registro.ShouldBeNull();
    }

    // RF-CONFLICT.2: mudancas em trechos diferentes (frontmatter local, corpo remoto) sao combinadas
    // automaticamente, sem perder nenhuma das duas.
    [Fact]
    public async Task MudancasEmTrechosDiferentes_ResolveAutomaticamenteEEnviaOMerge()
    {
        const string baseContent = "---\nstatus: rascunho\n---\ncorpo original";
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), baseContent, CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);
        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);

        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "---\nstatus: em revisao\n---\ncorpo original", CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        _cloudProvider.SimularMudancaRemota(registro!.CloudFileId!, "---\nstatus: rascunho\n---\ncorpo alterado remotamente", _timeProvider.GetUtcNow());

        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Modified), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var resultado = await _fileSystem.ReadAllTextAsync(VaultPath("nota.md"), CancellationToken.None);
        resultado.ShouldContain("status: em revisao");
        resultado.ShouldContain("corpo alterado remotamente");

        var registroFinal = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registroFinal!.Status.ShouldBe(SyncStatus.Synced);
    }

    // RF-CONFLICT.4: mesmo trecho editado nos dois lados - nenhuma versao e perdida, arquivo local
    // original nao e sobrescrito.
    [Fact]
    public async Task MesmoTrechoEditadoNosDoisLados_PreservaAmbasAsVersoesSemSobrescreverOriginal()
    {
        const string baseContent = "status: rascunho";
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), baseContent, CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);
        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);

        const string edicaoLocal = "status: em revisao";
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), edicaoLocal, CancellationToken.None);
        _timeProvider.Advance(TimeSpan.FromSeconds(5));
        _cloudProvider.SimularMudancaRemota(registro!.CloudFileId!, "status: concluido", _timeProvider.GetUtcNow());

        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Modified), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var conteudoLocalIntacto = await _fileSystem.ReadAllTextAsync(VaultPath("nota.md"), CancellationToken.None);
        conteudoLocalIntacto.ShouldBe(edicaoLocal); // original nunca fica orfao/vazio, nem e sobrescrito

        _indexStore.Conflicts.Count.ShouldBe(1);
        var conflito = _indexStore.Conflicts[0];
        (await _fileSystem.ReadAllTextAsync(conflito.LocalVersionPath, CancellationToken.None)).ShouldBe(edicaoLocal);
        (await _fileSystem.ReadAllTextAsync(conflito.RemoteVersionPath, CancellationToken.None)).ShouldBe("status: concluido");

        var registroFinal = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registroFinal!.Status.ShouldBe(SyncStatus.Conflict);
    }

    [Fact]
    public async Task EventoRedundante_NaoAlteraNadaNemChamaANuvemDeNovo()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "conteudo", CancellationToken.None);
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);
        var registroAntes = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);

        // mesmo conteudo, mesmo hash - evento "tocou" o arquivo sem mudanca real
        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Modified), CancellationToken.None);
        await _processor.DrainAsync(CancellationToken.None);

        var registroDepois = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registroDepois!.LastSyncedAt.ShouldBe(registroAntes!.LastSyncedAt);
    }

    // TC-06: erro 429/403 dispara backoff exponencial (FakeTimeProvider avanca sem esperar de verdade),
    // sem derrubar o processamento - eventualmente resolve com sucesso.
    [Fact]
    public async Task ErroDeCotaTransitorio_TentaDeNovoComBackoffEEventualmenteConcluiComSucesso()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "conteudo", CancellationToken.None);
        _cloudProvider.FalharProximaChamadaCom(new CloudQuotaExceededException("cota excedida"));

        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        var drainTask = _processor.DrainAsync(CancellationToken.None);

        // A primeira tentativa falha e agenda o backoff (base 1s); avancamos o relogio simulado para
        // destravar o Task.Delay sem esperar tempo real.
        await AvancarAteCompletar(drainTask, TimeSpan.FromSeconds(2));

        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registro.ShouldNotBeNull();
        registro.Status.ShouldBe(SyncStatus.Synced);
    }

    [Fact]
    public async Task ErroPersistente_EsgotaTentativasEMantemOItemNaFilaComoFalho()
    {
        await _fileSystem.WriteAllTextAsync(VaultPath("nota.md"), "conteudo", CancellationToken.None);
        _cloudProvider.FalharProximaChamadaCom(new CloudTransientException("erro 500"));
        _cloudProvider.FalharProximaChamadaCom(new CloudTransientException("erro 500"));
        _cloudProvider.FalharProximaChamadaCom(new CloudTransientException("erro 500"));

        await _processor.EnqueueAsync(new VaultChangeEvent("nota.md", SyncEventType.Created), CancellationToken.None);
        var drainTask = _processor.DrainAsync(CancellationToken.None);

        await AvancarAteCompletar(drainTask, TimeSpan.FromSeconds(70));

        var registro = await _indexStore.FindByLocalPathAsync("nota.md", CancellationToken.None);
        registro.ShouldBeNull(); // nunca chegou a ser upsertado - todas as 3 tentativas (MaxAttempts) falharam

        var itemNaFila = await _indexStore.PeekNextAsync(CancellationToken.None);
        itemNaFila.ShouldNotBeNull();
        itemNaFila.Attempts.ShouldBe(3);
    }

    private async Task AvancarAteCompletar(Task tarefa, TimeSpan passoMaximo)
    {
        var passo = TimeSpan.FromMilliseconds(50);
        var decorrido = TimeSpan.Zero;

        while (!tarefa.IsCompleted && decorrido < passoMaximo)
        {
            _timeProvider.Advance(passo);
            decorrido += passo;
            await Task.Yield();
        }

        await tarefa;
    }
}
