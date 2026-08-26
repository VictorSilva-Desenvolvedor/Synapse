using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync;
using Synapse.Tests.TestDoubles;

namespace Synapse.Tests.Sync;

public class RemoteChangesPollerTests
{
    private sealed class PagedCloudProviderStub : ICloudProvider
    {
        private readonly Dictionary<string, ChangesPage> _pages;
        private readonly List<string> _requestedTokens = [];

        public PagedCloudProviderStub(Dictionary<string, ChangesPage> pages) => _pages = pages;

        public IReadOnlyList<string> RequestedTokens => _requestedTokens;

        public Task<CloudFile> UploadAsync(string localPath, string remoteFolderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CloudFile> UpdateAsync(string cloudFileId, string localPath, CancellationToken ct) => throw new NotSupportedException();
        public Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string cloudFileId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CloudFile> GetMetadataAsync(string cloudFileId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> GetStartPageTokenAsync(CancellationToken ct) => Task.FromResult("page-1");

        public Task<ChangesPage> GetChangesAsync(string pageToken, CancellationToken ct)
        {
            _requestedTokens.Add(pageToken);
            return Task.FromResult(_pages[pageToken]);
        }
    }

    [Fact]
    public async Task RunOnce_PercorreTodasAsPaginasEPersisteOTokenNovoSoUmaVez()
    {
        var arquivo1 = new CloudFile("id-1", "nota1.md", "md5-1", DateTimeOffset.UtcNow, Trashed: false);
        var arquivo2 = new CloudFile("id-2", "nota2.md", "md5-2", DateTimeOffset.UtcNow, Trashed: false);
        var cloud = new PagedCloudProviderStub(new Dictionary<string, ChangesPage>
        {
            ["page-1"] = new ChangesPage([arquivo1], NextPageToken: "page-2", NewStartPageToken: null),
            ["page-2"] = new ChangesPage([arquivo2], NextPageToken: null, NewStartPageToken: "page-3-final"),
        });
        var index = new InMemorySyncIndexStore();
        var enfileirados = new List<VaultChangeEvent>();
        var poller = new RemoteChangesPoller(cloud, index, (evt, _) => { enfileirados.Add(evt); return Task.CompletedTask; });

        await poller.RunOnceAsync(CancellationToken.None);

        enfileirados.Count.ShouldBe(2);
        enfileirados[0].RelativePath.ShouldBe("nota1.md");
        enfileirados[1].RelativePath.ShouldBe("nota2.md");
        cloud.RequestedTokens.ShouldBe(["page-1", "page-2"]);
        (await index.GetChangesPageTokenAsync(CancellationToken.None)).ShouldBe("page-3-final");
    }

    [Fact]
    public async Task RunOnce_ArquivoJaIndexado_UsaOCaminhoLocalConhecidoEmVezDoNomeRemoto()
    {
        var index = new InMemorySyncIndexStore();
        await index.UpsertAsync(
            new SyncedFileRecord(0, "pasta/nota-original.md", "id-1", "hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SyncStatus.Synced),
            CancellationToken.None);

        var arquivo = new CloudFile("id-1", "nota-original.md", "md5-novo", DateTimeOffset.UtcNow.AddMinutes(1), Trashed: false);
        var cloud = new PagedCloudProviderStub(new Dictionary<string, ChangesPage> { ["page-1"] = new ChangesPage([arquivo], null, "token-2") });
        var enfileirados = new List<VaultChangeEvent>();
        var poller = new RemoteChangesPoller(cloud, index, (evt, _) => { enfileirados.Add(evt); return Task.CompletedTask; });

        await poller.RunOnceAsync(CancellationToken.None);

        enfileirados[0].RelativePath.ShouldBe("pasta/nota-original.md");
    }

    [Fact]
    public async Task RunOnce_ArquivoNaoIndexadoAinda_UsaONomeRemotoComoMelhorPalpite()
    {
        var arquivo = new CloudFile("id-novo", "nota-nova.md", "md5", DateTimeOffset.UtcNow, Trashed: false);
        var cloud = new PagedCloudProviderStub(new Dictionary<string, ChangesPage> { ["page-1"] = new ChangesPage([arquivo], null, "token-2") });
        var index = new InMemorySyncIndexStore();
        var enfileirados = new List<VaultChangeEvent>();
        var poller = new RemoteChangesPoller(cloud, index, (evt, _) => { enfileirados.Add(evt); return Task.CompletedTask; });

        await poller.RunOnceAsync(CancellationToken.None);

        enfileirados[0].RelativePath.ShouldBe("nota-nova.md");
    }

    [Fact]
    public async Task RunOnce_ArquivoTrashed_EnfileraEventoDeleted()
    {
        var arquivo = new CloudFile("id-1", "nota.md", "md5", DateTimeOffset.UtcNow, Trashed: true);
        var cloud = new PagedCloudProviderStub(new Dictionary<string, ChangesPage> { ["page-1"] = new ChangesPage([arquivo], null, "token-2") });
        var index = new InMemorySyncIndexStore();
        var enfileirados = new List<VaultChangeEvent>();
        var poller = new RemoteChangesPoller(cloud, index, (evt, _) => { enfileirados.Add(evt); return Task.CompletedTask; });

        await poller.RunOnceAsync(CancellationToken.None);

        enfileirados[0].EventType.ShouldBe(SyncEventType.Deleted);
    }

    [Fact]
    public async Task RunAsync_ExecutaUmCicloAoIniciarEDeNovoACadaIntervalo()
    {
        var timeProvider = new FakeTimeProvider();
        var cloud = new PagedCloudProviderStub(new Dictionary<string, ChangesPage> { ["page-1"] = new ChangesPage([], null, "page-1") });
        var index = new InMemorySyncIndexStore();
        var poller = new RemoteChangesPoller(cloud, index, (_, _) => Task.CompletedTask, TimeSpan.FromSeconds(60), timeProvider);

        using var cts = new CancellationTokenSource();
        var runTask = poller.RunAsync(cts.Token);

        await Task.Delay(100);
        cloud.RequestedTokens.Count.ShouldBeGreaterThanOrEqualTo(1);

        timeProvider.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(100);
        cloud.RequestedTokens.Count.ShouldBeGreaterThanOrEqualTo(2);

        cts.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => runTask);
    }
}
