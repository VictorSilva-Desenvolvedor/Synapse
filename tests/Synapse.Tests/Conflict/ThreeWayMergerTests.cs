using Shouldly;
using Synapse.Conflict;
using Synapse.Core.Ports;

namespace Synapse.Tests.Conflict;

public class ThreeWayMergerTests
{
    private readonly ThreeWayMerger _merger = new();

    [Fact]
    public void SemMudancaEmNenhumLado_RetornaConteudoOriginal()
    {
        const string baseContent = "linha 1\nlinha 2\nlinha 3";

        var resultado = _merger.Merge(baseContent, baseContent, baseContent);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe(baseContent);
    }

    [Fact]
    public void SoMudouLocal_RetornaConteudoLocal()
    {
        const string baseContent = "linha 1\nlinha 2\nlinha 3";
        const string local = "linha 1\nlinha 2 editada\nlinha 3";

        var resultado = _merger.Merge(baseContent, local, baseContent);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe(local);
    }

    [Fact]
    public void SoMudouRemoto_RetornaConteudoRemoto()
    {
        const string baseContent = "linha 1\nlinha 2\nlinha 3";
        const string remoto = "linha 1\nlinha 2\nlinha 3 editada";

        var resultado = _merger.Merge(baseContent, baseContent, remoto);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe(remoto);
    }

    // RF-CONFLICT.2 / TC-02: mudancas em partes diferentes do arquivo sao combinadas automaticamente.
    [Fact]
    public void MudancasEmTrechosDiferentes_SaoCombinadasAutomaticamente()
    {
        const string baseContent = "titulo\nintroducao\nconclusao";
        const string local = "titulo editado por A\nintroducao\nconclusao";
        const string remoto = "titulo\nintroducao\nconclusao editada por B";

        var resultado = _merger.Merge(baseContent, local, remoto);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe("titulo editado por A\nintroducao\nconclusao editada por B");
    }

    // RF-CONFLICT.4 / TC-03: mesmo trecho editado nos dois lados nao perde nenhuma versao.
    [Fact]
    public void MesmaLinhaEditadaNosDoisLados_RetornaNaoResolvivelPreservandoAmbasAsVersoes()
    {
        const string baseContent = "status: rascunho";
        const string local = "status: em revisao";
        const string remoto = "status: concluido";

        var resultado = _merger.Merge(baseContent, local, remoto);

        var naoResolvido = resultado.ShouldBeOfType<MergeResult.Unresolvable>();
        naoResolvido.LocalContent.ShouldBe(local);
        naoResolvido.RemoteContent.ShouldBe(remoto);
    }

    [Fact]
    public void InsercoesNoMesmoPontoDaBase_SaoTratadasComoConflito()
    {
        const string baseContent = "linha unica";
        const string local = "linha unica\ninserida por A";
        const string remoto = "linha unica\ninserida por B";

        var resultado = _merger.Merge(baseContent, local, remoto);

        resultado.ShouldBeOfType<MergeResult.Unresolvable>();
    }

    [Fact]
    public void InsercoesEmPontosDiferentes_SaoCombinadasAutomaticamente()
    {
        const string baseContent = "meio";
        const string local = "inicio adicionado por A\nmeio";
        const string remoto = "meio\nfim adicionado por B";

        var resultado = _merger.Merge(baseContent, local, remoto);

        var resolvido = resultado.ShouldBeOfType<MergeResult.Resolved>();
        resolvido.MergedContent.ShouldBe("inicio adicionado por A\nmeio\nfim adicionado por B");
    }
}
