using Shouldly;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class AutoLinkerTests
{
    [Fact]
    public void LinkExistingNotes_WhenNotesAreMentioned_ShouldInjectWikilinks()
    {
        var content = "Hoje estudei sobre Arquitetura Hexagonal e padrões de sincronização.";
        var existingNotes = new List<string> { "Arquitetura Hexagonal", "Sincronização Local" };

        var linked = AutoLinkerService.LinkExistingNotes(content, existingNotes);

        linked.ShouldBe("Hoje estudei sobre [[Arquitetura Hexagonal]] e padrões de sincronização.");
    }

    [Fact]
    public void LinkExistingNotes_WhenAlreadyWikilinked_ShouldNotDoubleLink()
    {
        var content = "Nota com link existente [[Arquitetura Hexagonal]].";
        var existingNotes = new List<string> { "Arquitetura Hexagonal" };

        var linked = AutoLinkerService.LinkExistingNotes(content, existingNotes);

        linked.ShouldBe("Nota com link existente [[Arquitetura Hexagonal]].");
    }

    [Fact]
    public void AppendConnectionsSection_WhenConnectedNotesProvided_ShouldAppendSection()
    {
        var content = "# Minha Nota\n\nConteúdo principal.";
        var connected = new List<string> { "Nota A", "Nota B" };

        var result = AutoLinkerService.AppendConnectionsSection(content, connected);

        result.ShouldContain("## Conexões & Notas Relacionadas");
        result.ShouldContain("- [[Nota A]]");
        result.ShouldContain("- [[Nota B]]");
    }
}
