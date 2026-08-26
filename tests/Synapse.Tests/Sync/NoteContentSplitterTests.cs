using Shouldly;
using Synapse.Sync;

namespace Synapse.Tests.Sync;

public class NoteContentSplitterTests
{
    [Fact]
    public void Split_ComFrontmatter_SeparaFrontmatterDoCorpo()
    {
        const string nota = "---\nstatus: rascunho\ntags: [a, b]\n---\nCorpo da nota.\nSegunda linha.";

        var (frontmatter, corpo) = NoteContentSplitter.Split(nota);

        frontmatter.ShouldBe("status: rascunho\ntags: [a, b]");
        corpo.ShouldBe("Corpo da nota.\nSegunda linha.");
    }

    [Fact]
    public void Split_SemFrontmatter_RetornaFrontmatterVazioECorpoIntegral()
    {
        const string nota = "Nota sem frontmatter nenhum.";

        var (frontmatter, corpo) = NoteContentSplitter.Split(nota);

        frontmatter.ShouldBeEmpty();
        corpo.ShouldBe(nota);
    }

    [Fact]
    public void Split_ComDelimitadorAbertoSemFechar_TrataComoSemFrontmatter()
    {
        const string nota = "---\nisso nao fecha o frontmatter";

        var (frontmatter, corpo) = NoteContentSplitter.Split(nota);

        frontmatter.ShouldBeEmpty();
        corpo.ShouldBe(nota);
    }

    [Fact]
    public void Join_ComFrontmatterVazio_RetornaSoOCorpo()
    {
        var resultado = NoteContentSplitter.Join(string.Empty, "corpo puro");

        resultado.ShouldBe("corpo puro");
    }

    [Fact]
    public void Join_ComFrontmatter_RemontaComDelimitadores()
    {
        var resultado = NoteContentSplitter.Join("status: rascunho", "corpo");

        resultado.ShouldBe("---\nstatus: rascunho\n---\ncorpo");
    }

    [Fact]
    public void SplitEJoin_SaoRoundTripParaUmaNotaTipica()
    {
        const string nota = "---\nstatus: rascunho\n---\nCorpo da nota.";

        var (frontmatter, corpo) = NoteContentSplitter.Split(nota);
        var reconstruida = NoteContentSplitter.Join(frontmatter, corpo);

        reconstruida.ShouldBe(nota);
    }
}
