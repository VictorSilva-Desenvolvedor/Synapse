using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Rules;
using Synapse.Tests.TestDoubles;

namespace Synapse.Tests.Rules;

public class RuleEngineTests
{
    private const string RulesPath = "/vault/.synapse/regras.yaml";
    private const string VaultRoot = "/vault";

    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly FakeTimeProvider _timeProvider = new();
    private RuleEngine _engine = null!;

    private RuleEngine CriarEngine()
    {
        _engine = new RuleEngine(_fileSystem, VaultRoot, _timeProvider);
        return _engine;
    }

    private static NoteContext Nota(string relativePath, string frontmatterYaml = "") =>
        new(relativePath, frontmatterYaml, DateTimeOffset.UtcNow);

    [Fact]
    public async Task LoadRules_ArquivoVazio_NaoProduzNenhumaAcao()
    {
        await _fileSystem.WriteAllTextAsync(RulesPath, "", CancellationToken.None);
        var engine = CriarEngine();

        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);
        var acoes = await engine.EvaluateAsync(Nota("qualquer.md"), CancellationToken.None);

        acoes.ShouldBeEmpty();
    }

    // RF-RULES.3 / US-RULES.3: notas novas em certas pastas recebem tags automaticamente.
    [Fact]
    public async Task RegraAutoTag_NotaNaPastaOrigem_RecebeAsTagsConfiguradas()
    {
        const string yaml = """
            regras:
              - tipo: auto_tag
                pasta_origem: "Inbox/"
                tags: ["#inbox", "#revisar"]
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Inbox/nota-nova.md"), CancellationToken.None);

        acoes.Count.ShouldBe(1);
        var addTags = acoes[0].ShouldBeOfType<RuleAction.AddTags>();
        addTags.TargetPath.ShouldBe("Inbox/nota-nova.md");
        addTags.Tags.ShouldBe(["#inbox", "#revisar"]);
    }

    [Fact]
    public async Task RegraAutoTag_NotaForaDaPastaOrigem_NaoProduzAcao()
    {
        const string yaml = """
            regras:
              - tipo: auto_tag
                pasta_origem: "Inbox/"
                tags: ["#inbox"]
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Projetos/nota.md"), CancellationToken.None);

        acoes.ShouldBeEmpty();
    }

    // RF-RULES.4 / US-RULES.4: notas mudam de pasta com base no frontmatter.
    [Fact]
    public async Task RegraMoverPorStatus_FrontmatterComValorEsperado_ProduzMoveNote()
    {
        const string yaml = """
            regras:
              - tipo: mover_por_status
                campo_frontmatter: "status"
                valor: "concluído"
                pasta_destino: "Arquivo/"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Projetos/tarefa.md", "status: concluído"), CancellationToken.None);

        acoes.Count.ShouldBe(1);
        var move = acoes[0].ShouldBeOfType<RuleAction.MoveNote>();
        move.FromPath.ShouldBe("Projetos/tarefa.md");
        move.ToPath.ShouldBe("Arquivo/tarefa.md");
    }

    [Fact]
    public async Task RegraMoverPorStatus_FrontmatterComOutroValor_NaoProduzAcao()
    {
        const string yaml = """
            regras:
              - tipo: mover_por_status
                campo_frontmatter: "status"
                valor: "concluído"
                pasta_destino: "Arquivo/"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Projetos/tarefa.md", "status: rascunho"), CancellationToken.None);

        acoes.ShouldBeEmpty();
    }

    // RF-RULES.2 / US-RULES.2: nota diaria criada automaticamente, resolvendo o placeholder de data.
    [Fact]
    public async Task RegraNotaDiaria_ArquivoDeHojeNaoExiste_ProduzCreateNoteComCaminhoResolvido()
    {
        const string yaml = """
            regras:
              - tipo: nota_diaria
                caminho: "Diario/{{data:yyyy-MM-dd}}.md"
                template: "Templates/nota-diaria.md"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("qualquer-outra-nota.md"), CancellationToken.None);

        acoes.Count.ShouldBe(1);
        var createNote = acoes[0].ShouldBeOfType<RuleAction.CreateNote>();
        createNote.TargetPath.ShouldBe("Diario/2026-08-26.md");
        createNote.TemplatePath.ShouldBe("Templates/nota-diaria.md");
    }

    [Fact]
    public async Task RegraNotaDiaria_ArquivoDeHojeJaExiste_NaoProduzAcaoDeNovo()
    {
        const string yaml = """
            regras:
              - tipo: nota_diaria
                caminho: "Diario/{{data:yyyy-MM-dd}}.md"
                template: "Templates/nota-diaria.md"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        _timeProvider.SetUtcNow(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
        await _fileSystem.WriteAllTextAsync($"{VaultRoot}/Diario/2026-08-26.md", "ja existe", CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("qualquer-outra-nota.md"), CancellationToken.None);

        acoes.ShouldBeEmpty();
    }

    // RF-RULES.1: multiplas regras avaliadas juntas, cada uma contribuindo sua propria acao.
    [Fact]
    public async Task MultiplasRegras_SaoAvaliadasIndependentemente()
    {
        const string yaml = """
            regras:
              - tipo: auto_tag
                pasta_origem: "Inbox/"
                tags: ["#inbox"]
              - tipo: mover_por_status
                campo_frontmatter: "status"
                valor: "concluído"
                pasta_destino: "Arquivo/"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Inbox/tarefa.md", "status: concluído"), CancellationToken.None);

        acoes.Count.ShouldBe(2);
        acoes.ShouldContain(a => a is RuleAction.AddTags);
        acoes.ShouldContain(a => a is RuleAction.MoveNote);
    }

    // RF-RULES.5: garantia do proprio tipo RuleAction (ja testada em Synapse.Tests.Core.Ports), mas
    // confirmamos aqui tambem que o motor de regras nunca produz um caso alem dos tres conhecidos.
    [Fact]
    public async Task EvaluateAsync_NuncaProduzUmaAcaoQueNaoSejaUmDosTresCasosConhecidos()
    {
        const string yaml = """
            regras:
              - tipo: auto_tag
                pasta_origem: "Inbox/"
                tags: ["#inbox"]
              - tipo: mover_por_status
                campo_frontmatter: "status"
                valor: "concluído"
                pasta_destino: "Arquivo/"
              - tipo: nota_diaria
                caminho: "Diario/{{data:yyyy-MM-dd}}.md"
                template: "Templates/nota-diaria.md"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Inbox/tarefa.md", "status: concluído"), CancellationToken.None);

        acoes.All(a => a is RuleAction.AddTags or RuleAction.MoveNote or RuleAction.CreateNote).ShouldBeTrue();
    }

    [Fact]
    public async Task LoadRules_TipoDeRegraDesconhecido_LancaFormatException()
    {
        const string yaml = """
            regras:
              - tipo: apagar_tudo
                pasta_origem: "Inbox/"
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();

        await Should.ThrowAsync<FormatException>(() => engine.LoadRulesAsync(RulesPath, CancellationToken.None));
    }

    [Fact]
    public async Task LoadRules_RegraSemCampoObrigatorio_LancaFormatException()
    {
        const string yaml = """
            regras:
              - tipo: auto_tag
                tags: ["#inbox"]
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yaml, CancellationToken.None);
        var engine = CriarEngine();

        await Should.ThrowAsync<FormatException>(() => engine.LoadRulesAsync(RulesPath, CancellationToken.None));
    }

    [Fact]
    public async Task LoadRules_ChamadoDeNovo_SubstituiAsRegrasAnteriores()
    {
        const string yamlInicial = """
            regras:
              - tipo: auto_tag
                pasta_origem: "Inbox/"
                tags: ["#inbox"]
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yamlInicial, CancellationToken.None);
        var engine = CriarEngine();
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        const string yamlRecarregado = """
            regras: []
            """;
        await _fileSystem.WriteAllTextAsync(RulesPath, yamlRecarregado, CancellationToken.None);
        await engine.LoadRulesAsync(RulesPath, CancellationToken.None);

        var acoes = await engine.EvaluateAsync(Nota("Inbox/nota.md"), CancellationToken.None);
        acoes.ShouldBeEmpty();
    }
}
