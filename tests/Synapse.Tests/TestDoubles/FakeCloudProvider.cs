using System.Security.Cryptography;
using System.Text;
using Synapse.Core.Ports;

namespace Synapse.Tests.TestDoubles;

/// <summary>
/// Dublê de ICloudProvider (Plano de Testes seção 6.1): mantém arquivos em memória (via o mesmo
/// IFileSystem usado pelo resto do teste, para ler o que foi "enviado" e escrever o que foi "baixado"),
/// simula changes.list devolvendo só o que mudou desde o pageToken, e é configurável para lançar
/// exceções sob demanda (seção 6.3, usado para TC-06 - backoff exponencial).
/// </summary>
public sealed class FakeCloudProvider : ICloudProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, string> _contentByFileId = [];
    private readonly Dictionary<string, CloudFile> _metaByFileId = [];
    private readonly List<CloudFile> _changesLog = [];
    private readonly Queue<Exception> _scheduledFailures = new();
    private int _nextId = 1;

    public int UploadCount { get; private set; }
    public int UpdateCount { get; private set; }
    public int DownloadCount { get; private set; }
    public int GetMetadataCount { get; private set; }

    public FakeCloudProvider(IFileSystem fileSystem, TimeProvider? timeProvider = null)
    {
        _fileSystem = fileSystem;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void FalharProximaChamadaCom(Exception exception) => _scheduledFailures.Enqueue(exception);

    private void LancarSeAgendado()
    {
        if (_scheduledFailures.Count > 0)
            throw _scheduledFailures.Dequeue();
    }

    public async Task<CloudFile> UploadAsync(string localPath, string remoteFolderId, CancellationToken ct)
    {
        LancarSeAgendado();
        UploadCount++;
        var content = await _fileSystem.ReadAllTextAsync(localPath, ct);
        var id = $"fake-{_nextId++}";
        var meta = new CloudFile(id, Path.GetFileName(localPath), Md5(content), _timeProvider.GetUtcNow(), Trashed: false);

        _contentByFileId[id] = content;
        _metaByFileId[id] = meta;
        _changesLog.Add(meta);
        return meta;
    }

    public async Task<CloudFile> UpdateAsync(string cloudFileId, string localPath, CancellationToken ct)
    {
        LancarSeAgendado();
        UpdateCount++;
        if (!_metaByFileId.TryGetValue(cloudFileId, out var existing))
            throw new CloudNotFoundException($"Arquivo {cloudFileId} não existe no FakeCloudProvider.");

        var content = await _fileSystem.ReadAllTextAsync(localPath, ct);
        var meta = existing with { Md5Checksum = Md5(content), ModifiedTime = _timeProvider.GetUtcNow() };

        _contentByFileId[cloudFileId] = content;
        _metaByFileId[cloudFileId] = meta;
        _changesLog.Add(meta);
        return meta;
    }

    public async Task DownloadAsync(string cloudFileId, string destinationPath, CancellationToken ct)
    {
        LancarSeAgendado();
        DownloadCount++;
        if (!_contentByFileId.TryGetValue(cloudFileId, out var content))
            throw new CloudNotFoundException($"Arquivo {cloudFileId} não existe no FakeCloudProvider.");

        await _fileSystem.WriteAllTextAsync(destinationPath, content, ct);
    }

    public Task DeleteAsync(string cloudFileId, CancellationToken ct)
    {
        LancarSeAgendado();
        _contentByFileId.Remove(cloudFileId);

        if (_metaByFileId.TryGetValue(cloudFileId, out var meta))
        {
            var trashed = meta with { Trashed = true };
            _metaByFileId[cloudFileId] = trashed;
            _changesLog.Add(trashed);
        }

        return Task.CompletedTask;
    }

    public Task<CloudFile> GetMetadataAsync(string cloudFileId, CancellationToken ct)
    {
        LancarSeAgendado();
        GetMetadataCount++;
        if (!_metaByFileId.TryGetValue(cloudFileId, out var meta))
            throw new CloudNotFoundException($"Arquivo {cloudFileId} não existe no FakeCloudProvider.");

        return Task.FromResult(meta);
    }

    public Task<string> GetStartPageTokenAsync(CancellationToken ct)
    {
        LancarSeAgendado();
        return Task.FromResult("0");
    }

    public Task<ChangesPage> GetChangesAsync(string pageToken, CancellationToken ct)
    {
        LancarSeAgendado();
        var since = int.Parse(pageToken);
        var changed = _changesLog.Skip(since).ToList();
        var newToken = _changesLog.Count.ToString();
        return Task.FromResult(new ChangesPage(changed, NextPageToken: null, NewStartPageToken: newToken));
    }

    /// <summary>Simula uma mudança feita "do outro lado" (outro dispositivo), sem passar por Upload/UpdateAsync.</summary>
    public CloudFile SimularMudancaRemota(string cloudFileId, string novoConteudo, DateTimeOffset modifiedTime)
    {
        var existing = _metaByFileId[cloudFileId];
        var meta = existing with { Md5Checksum = Md5(novoConteudo), ModifiedTime = modifiedTime };

        _contentByFileId[cloudFileId] = novoConteudo;
        _metaByFileId[cloudFileId] = meta;
        _changesLog.Add(meta);
        return meta;
    }

    private static string Md5(string content) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
