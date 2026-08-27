using NSubstitute;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Rules;

namespace Synapse.Tests.Rules;

public class RuleExecutorAdvancedTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly RuleExecutor _executor;
    private const string VaultPath = "C:\\Vault";

    public RuleExecutorAdvancedTests()
    {
        _executor = new RuleExecutor(_fileSystem, VaultPath);
    }

    [Fact]
    public async Task ExecuteAppendContentAsync_ShouldAppendTextAtEndOfFile()
    {
        var targetPath = "Notas\\Projeto.md";
        var fullPath = Path.Combine(VaultPath, targetPath);

        _fileSystem.ExistsAsync(fullPath, Arg.Any<CancellationToken>()).Returns(true);
        _fileSystem.ReadAllTextAsync(fullPath, Arg.Any<CancellationToken>())
            .Returns("# Projeto X\nConteudo existente.");

        var action = new RuleAction.AppendContent(targetPath, "## Rodapé Adicionado");
        await _executor.ExecuteActionAsync(action);

        await _fileSystem.Received(1).WriteAllTextAsync(
            fullPath,
            Arg.Is<string>(s => s.Contains("# Projeto X") && s.EndsWith("## Rodapé Adicionado\n")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecutePrependContentAsync_WithFrontmatter_ShouldInsertAfterFrontmatter()
    {
        var targetPath = "Notas\\Artigo.md";
        var fullPath = Path.Combine(VaultPath, targetPath);

        var originalContent = "---\ntitulo: Artigo\nstatus: rascunho\n---\n# Conteúdo original";
        _fileSystem.ExistsAsync(fullPath, Arg.Any<CancellationToken>()).Returns(true);
        _fileSystem.ReadAllTextAsync(fullPath, Arg.Any<CancellationToken>()).Returns(originalContent);

        var action = new RuleAction.PrependContent(targetPath, "> [!WARNING]\n> Nota em revisão.");
        await _executor.ExecuteActionAsync(action);

        await _fileSystem.Received(1).WriteAllTextAsync(
            fullPath,
            Arg.Is<string>(s => s.StartsWith("---\ntitulo: Artigo\nstatus: rascunho\n---") && s.Contains("> [!WARNING]") && s.Contains("# Conteúdo original")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteExtractTasksAsync_ShouldExtractOpenTasksOnly()
    {
        var sourcePath = "Notas\\Reuniao.md";
        var dailyPath = "Diario\\2026-08-27.md";

        var sourceFullPath = Path.Combine(VaultPath, sourcePath);
        var dailyFullPath = Path.Combine(VaultPath, dailyPath);

        var sourceContent = "# Reunião\n- [ ] Comprar café\n- [x] Enviar relatório\n- [ ] Atualizar backlog";
        _fileSystem.ExistsAsync(sourceFullPath, Arg.Any<CancellationToken>()).Returns(true);
        _fileSystem.ReadAllTextAsync(sourceFullPath, Arg.Any<CancellationToken>()).Returns(sourceContent);

        _fileSystem.ExistsAsync(dailyFullPath, Arg.Any<CancellationToken>()).Returns(false);

        var action = new RuleAction.ExtractTasks(sourcePath, dailyPath);
        await _executor.ExecuteActionAsync(action);

        await _fileSystem.Received(1).WriteAllTextAsync(
            dailyFullPath,
            Arg.Is<string>(s => s.Contains("- [ ] Comprar café") && s.Contains("- [ ] Atualizar backlog") && !s.Contains("Enviar relatório")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteRenameNoteAsync_ShouldRenameFileSafely()
    {
        var fromPath = "Inbox\\NovaIdeia.md";
        var fromFullPath = Path.Combine(VaultPath, fromPath);
        var expectedToPath = Path.Combine(VaultPath, "Inbox", $"{DateTime.UtcNow:yyyy-MM-dd}-NovaIdeia.md");

        _fileSystem.ExistsAsync(fromFullPath, Arg.Any<CancellationToken>()).Returns(true);
        _fileSystem.ExistsAsync(expectedToPath, Arg.Any<CancellationToken>()).Returns(false);
        _fileSystem.ReadAllTextAsync(fromFullPath, Arg.Any<CancellationToken>()).Returns("# Nova Ideia");

        var action = new RuleAction.RenameNote(fromPath, "{{date}}-{{title}}");
        await _executor.ExecuteActionAsync(action);

        await _fileSystem.Received(1).WriteAllTextAsync(expectedToPath, "# Nova Ideia", Arg.Any<CancellationToken>());
        await _fileSystem.Received(1).DeleteAsync(fromFullPath, Arg.Any<CancellationToken>());
    }
}
