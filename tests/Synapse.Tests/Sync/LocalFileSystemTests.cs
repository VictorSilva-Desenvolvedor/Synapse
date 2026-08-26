using Shouldly;
using Synapse.Sync;

namespace Synapse.Tests.Sync;

// Nivel de integracao: wrapper fino sobre System.IO, testado contra um diretorio temporario real (sem
// dependencia externa, ao contrario de GoogleDriveProvider).
public class LocalFileSystemTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileSystem _fileSystem = new();

    public LocalFileSystemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"synapse-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task WriteEReadAllText_FazemRoundTrip()
    {
        var caminho = Path.Combine(_root, "nota.md");

        await _fileSystem.WriteAllTextAsync(caminho, "conteudo", CancellationToken.None);
        var lido = await _fileSystem.ReadAllTextAsync(caminho, CancellationToken.None);

        lido.ShouldBe("conteudo");
    }

    [Fact]
    public async Task WriteAllText_CriaDiretoriosIntermediariosSeNaoExistirem()
    {
        var caminho = Path.Combine(_root, "subpasta", "aninhada", "nota.md");

        await _fileSystem.WriteAllTextAsync(caminho, "conteudo", CancellationToken.None);

        File.Exists(caminho).ShouldBeTrue();
    }

    [Fact]
    public async Task ExistsAsync_RefleteOEstadoRealDoDisco()
    {
        var caminho = Path.Combine(_root, "nota.md");

        (await _fileSystem.ExistsAsync(caminho, CancellationToken.None)).ShouldBeFalse();

        await _fileSystem.WriteAllTextAsync(caminho, "x", CancellationToken.None);
        (await _fileSystem.ExistsAsync(caminho, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemoveOArquivo()
    {
        var caminho = Path.Combine(_root, "nota.md");
        await _fileSystem.WriteAllTextAsync(caminho, "x", CancellationToken.None);

        await _fileSystem.DeleteAsync(caminho, CancellationToken.None);

        File.Exists(caminho).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_QuandoArquivoNaoExiste_NaoLancaExcecao()
    {
        var caminho = Path.Combine(_root, "nunca-existiu.md");

        await Should.NotThrowAsync(() => _fileSystem.DeleteAsync(caminho, CancellationToken.None));
    }
}
