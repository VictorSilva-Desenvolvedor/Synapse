using Shouldly;
using Synapse.Core.Ports;

namespace Synapse.Tests.Core.Ports;

public class MergeResultTests
{
    [Fact]
    public void Resolved_CarregaConteudoMesclado()
    {
        MergeResult resultado = new MergeResult.Resolved("conteudo final mesclado");

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe("conteudo final mesclado");
    }

    [Fact]
    public void Unresolvable_PreservaAsDuasVersoesSemPerda()
    {
        MergeResult resultado = new MergeResult.Unresolvable("versao local", "versao remota");

        var naoResolvido = resultado.ShouldBeOfType<MergeResult.Unresolvable>();
        naoResolvido.LocalContent.ShouldBe("versao local");
        naoResolvido.RemoteContent.ShouldBe("versao remota");
    }

    [Fact]
    public void PatternMatching_DistingueOsDoisCasos()
    {
        MergeResult resolvido = new MergeResult.Resolved("x");
        MergeResult naoResolvido = new MergeResult.Unresolvable("a", "b");

        Classificar(resolvido).ShouldBe("resolvido");
        Classificar(naoResolvido).ShouldBe("nao-resolvido");

        static string Classificar(MergeResult r) => r switch
        {
            MergeResult.Resolved => "resolvido",
            MergeResult.Unresolvable => "nao-resolvido",
            _ => throw new InvalidOperationException("MergeResult com caso não coberto - contrato mudou.")
        };
    }
}
