using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Rules;
using Synapse.Tests.TestDoubles;

namespace Synapse.Tests.Rules;

public class RuleExecutorTests
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 8, 27, 10, 30, 0, TimeSpan.Zero));
    private readonly string _vaultRoot = "C:/vault";
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task ExecuteActionAsync_CreateNote_ShouldResolvePlaceholdersAndCreateFile()
    {
        var executor = new RuleExecutor(_fileSystem, _vaultRoot, _timeProvider);
        var action = new RuleAction.CreateNote("Diario/2026-08-27.md", "# Nota Diaria - {{date}} {{time}}\nCriado em {{datetime}}");

        await executor.ExecuteActionAsync(action, _ct);

        var fullPath = "C:/vault/Diario/2026-08-27.md";
        (await _fileSystem.ExistsAsync(fullPath, _ct)).ShouldBeTrue();

        var content = await _fileSystem.ReadAllTextAsync(fullPath, _ct);
        content.ShouldContain("2026-08-27");
        content.ShouldContain("10:30:00");
    }

    [Fact]
    public async Task ExecuteActionAsync_CreateNote_WhenFileAlreadyExists_ShouldNotOverwrite()
    {
        var executor = new RuleExecutor(_fileSystem, _vaultRoot, _timeProvider);
        var fullPath = "C:/vault/Diario/2026-08-27.md";
        await _fileSystem.WriteAllTextAsync(fullPath, "# Conteudo Original Ja Existente", _ct);

        var action = new RuleAction.CreateNote("Diario/2026-08-27.md", "# Novo Template");
        await executor.ExecuteActionAsync(action, _ct);

        var content = await _fileSystem.ReadAllTextAsync(fullPath, _ct);
        content.ShouldBe("# Conteudo Original Ja Existente");
    }

    [Fact]
    public async Task ExecuteActionAsync_AddTags_ShouldInjectTagsIntoNote()
    {
        var executor = new RuleExecutor(_fileSystem, _vaultRoot, _timeProvider);
        var fullPath = "C:/vault/Projetos/Nota1.md";
        await _fileSystem.WriteAllTextAsync(fullPath, "# Projeto 1\nDetalhes do projeto.", _ct);

        var action = new RuleAction.AddTags("Projetos/Nota1.md", ["projeto", "ativo"]);
        await executor.ExecuteActionAsync(action, _ct);

        var content = await _fileSystem.ReadAllTextAsync(fullPath, _ct);
        content.ShouldContain("tags:");
        content.ShouldContain("projeto");
        content.ShouldContain("ativo");
        content.ShouldContain("# Projeto 1");
    }

    [Fact]
    public async Task ExecuteActionAsync_MoveNote_ShouldMoveFileWithoutLosingContent()
    {
        var executor = new RuleExecutor(_fileSystem, _vaultRoot, _timeProvider);
        var fromPath = "C:/vault/Inbox/Nota.md";
        var toPath = "C:/vault/Concluidos/Nota.md";
        await _fileSystem.WriteAllTextAsync(fromPath, "---\nstatus: feito\n---\n# Nota Concluida", _ct);

        var action = new RuleAction.MoveNote("Inbox/Nota.md", "Concluidos/Nota.md");
        await executor.ExecuteActionAsync(action, _ct);

        (await _fileSystem.ExistsAsync(fromPath, _ct)).ShouldBeFalse();
        (await _fileSystem.ExistsAsync(toPath, _ct)).ShouldBeTrue();

        var destContent = await _fileSystem.ReadAllTextAsync(toPath, _ct);
        destContent.ShouldContain("status: feito");
        destContent.ShouldContain("# Nota Concluida");
    }
}
