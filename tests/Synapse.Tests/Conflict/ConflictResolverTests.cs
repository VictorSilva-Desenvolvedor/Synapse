using Shouldly;
using Synapse.Conflict;
using Synapse.Core.Ports;

namespace Synapse.Tests.Conflict;

public class ConflictResolverTests
{
    private readonly IConflictResolver _resolver = new ConflictResolver();

    [Fact]
    public void TryMergeBody_DelegaParaOThreeWayMerger()
    {
        var resultado = _resolver.TryMergeBody("base", "local editado", "base");

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe("local editado");
    }

    [Fact]
    public void TryMergeFrontmatter_DelegaParaOFrontmatterMerger()
    {
        var resultado = _resolver.TryMergeFrontmatter("status: rascunho", "status: concluido", "status: rascunho");

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldContain("status: concluido");
    }
}
