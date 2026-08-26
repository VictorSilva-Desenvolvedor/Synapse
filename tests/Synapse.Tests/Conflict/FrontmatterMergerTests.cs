using Shouldly;
using Synapse.Conflict;
using Synapse.Core.Ports;

namespace Synapse.Tests.Conflict;

public class FrontmatterMergerTests
{
    private readonly FrontmatterMerger _merger = new();

    // RF-CONFLICT.3 / TC-09: chaves nao conflitantes alteradas em lados diferentes sao combinadas.
    [Fact]
    public void ChavesAlteradasEmLadosDiferentes_SaoCombinadas()
    {
        const string baseYaml = "status: rascunho\nprioridade: baixa";
        const string local = "status: em revisao\nprioridade: baixa";
        const string remoto = "status: rascunho\nprioridade: alta";

        var resultado = _merger.Merge(baseYaml, local, remoto);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldContain("status: em revisao");
        resolvido.MergedContent.ShouldContain("prioridade: alta");
    }

    [Fact]
    public void ChaveInalteradaNosDoisLados_PermaneceComValorDaBase()
    {
        const string baseYaml = "status: rascunho";

        var resultado = _merger.Merge(baseYaml, baseYaml, baseYaml);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldContain("status: rascunho");
    }

    [Fact]
    public void ChaveAlteradaParaValoresDiferentesNosDoisLados_RetornaConflito()
    {
        const string baseYaml = "status: rascunho";
        const string local = "status: em revisao";
        const string remoto = "status: concluido";

        var resultado = _merger.Merge(baseYaml, local, remoto);

        var naoResolvido = resultado.ShouldBeOfType<MergeResult.Unresolvable>();
        naoResolvido.LocalContent.ShouldBe(local);
        naoResolvido.RemoteContent.ShouldBe(remoto);
    }

    [Fact]
    public void ChaveAlteradaParaOMesmoValorNosDoisLados_NaoEConsideradaConflito()
    {
        const string baseYaml = "status: rascunho";
        const string local = "status: concluido";
        const string remoto = "status: concluido";

        var resultado = _merger.Merge(baseYaml, local, remoto);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldContain("status: concluido");
    }

    [Fact]
    public void ChaveAdicionadaSoLocalmente_EhIncluidaNoMerge()
    {
        const string baseYaml = "status: rascunho";
        const string local = "status: rascunho\ntags: projeto";

        var resultado = _merger.Merge(baseYaml, local, baseYaml);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldContain("tags: projeto");
    }

    [Fact]
    public void ChaveRemovidaSoLocalmente_NaoApareceNoMerge()
    {
        const string baseYaml = "status: rascunho\ntags: projeto";
        const string local = "status: rascunho";

        var resultado = _merger.Merge(baseYaml, local, baseYaml);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldNotContain("tags");
    }

    [Fact]
    public void FrontmatterVazio_NaoLancaExcecao()
    {
        var resultado = _merger.Merge("", "", "");

        resultado.ShouldBeOfType<MergeResult.Resolved>();
    }
}
