using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;
using Synapse.Search;

namespace Synapse.Tests.Brain;

public class VaultRagEngineTests : IDisposable
{
    private readonly string _tempVaultDir;

    public VaultRagEngineTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-rag-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task SearchAsync_ShouldRankNotesBySimilarityScore()
    {
        var note1Path = Path.Combine(_tempVaultDir, "Zettelkasten.md");
        var note2Path = Path.Combine(_tempVaultDir, "Receitas.md");

        await File.WriteAllTextAsync(note1Path, "Anotações atômicas e PKM com Zettelkasten.");
        await File.WriteAllTextAsync(note2Path, "Receita de bolo de chocolate e café.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        // Vetor de consulta
        mockEmbedding.GenerateEmbeddingAsync("segundo cerebro", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0.9f, 0f }));

        // Vetor da nota 1 (alta similaridade)
        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Zettelkasten")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.95f, 0.85f, 0f }));

        // Vetor da nota 2 (baixa similaridade)
        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Receita")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0f, 0.1f, 0.9f }));

        var mockAi = Substitute.For<IBrainAiProvider>();

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);
        var results = await ragEngine.SearchAsync("segundo cerebro", _tempVaultDir, topK: 2);

        results.Count.ShouldBe(2);
        results[0].Title.ShouldBe("Zettelkasten");
        results[0].SimilarityScore.ShouldBeGreaterThan(results[1].SimilarityScore);
    }

    [Fact]
    public async Task AskVaultAsync_ShouldAugmentPromptWithTopNotesAndReturnAnswer()
    {
        var notePath = Path.Combine(_tempVaultDir, "Arquitetura.md");
        await File.WriteAllTextAsync(notePath, "O Synapse adota a Arquitetura Hexagonal (Ports and Adapters).");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 1f, 1f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.AskQuestionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("O projeto utiliza a [[Arquitetura]] Hexagonal."));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);
        var answer = await ragEngine.AskVaultAsync("Qual é o padrão arquitetural do Synapse?", _tempVaultDir);

        answer.Answer.ShouldContain("Hexagonal");
        answer.Sources.Count.ShouldBeGreaterThan(0);
        answer.Sources[0].Title.ShouldBe("Arquitetura");
    }

    [Fact]
    public async Task AskVaultAsync_WhenAiProviderFails_ShouldPropagateExceptionWithoutLeakingPrompt()
    {
        var notePath = Path.Combine(_tempVaultDir, "Segredo.md");
        await File.WriteAllTextAsync(notePath, "Conteúdo sensível que não deve vazar em nenhuma mensagem de erro visível.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 1f, 1f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.AskQuestionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new InvalidOperationException("Gemini indisponível no momento.")));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            async () => await ragEngine.AskVaultAsync("O que tem na nota secreta?", _tempVaultDir));

        ex.Message.ShouldNotContain("Notas do cofre relevantes");
        ex.Message.ShouldNotContain("Conteúdo sensível");
        ex.Message.ShouldContain("Gemini indisponível");
    }

    [Fact]
    public async Task SaveAnswerAsNoteAsync_ShouldCreateMarkdownFileWithFrontmatterAndWikilinks()
    {
        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        var answer = new RagAnswer(
            Question: "Como funciona a arquitetura hexagonal?",
            Answer: "A arquitetura hexagonal isola o núcleo de domínio de detalhes de infraestrutura.",
            Sources:
            [
                new SemanticSearchResult("Arquitetura/Hexagonal.md", "Hexagonal", "Isolamento de domínio.", 0.95f),
                new SemanticSearchResult("Conceitos/PortsAdapters.md", "PortsAdapters", "Portas e adaptadores.", 0.88f)
            ]);

        var relativePath = await ragEngine.SaveAnswerAsNoteAsync(answer, _tempVaultDir);

        relativePath.ShouldStartWith("Brain/Conversas/");
        relativePath.ShouldEndWith(".md");

        var fullPath = Path.Combine(_tempVaultDir, relativePath);
        File.Exists(fullPath).ShouldBeTrue();

        var content = await File.ReadAllTextAsync(fullPath);
        content.ShouldContain("titulo: \"Como funciona a arquitetura hexagonal?\"");
        content.ShouldContain("categoria: \"Chat com o Cofre\"");
        content.ShouldContain("status: processado");
        content.ShouldContain("tags:");
        content.ShouldContain("- chat-cofre");
        content.ShouldContain("# Como funciona a arquitetura hexagonal?");
        content.ShouldContain("A arquitetura hexagonal isola o núcleo de domínio de detalhes de infraestrutura.");
        content.ShouldContain("### Fontes Consultadas");
        content.ShouldContain("- [[Hexagonal]]");
        content.ShouldContain("- [[PortsAdapters]]");
    }

    [Fact]
    public async Task SaveAnswerAsNoteAsync_WhenDuplicateQuestion_ShouldAppendNumericSuffix()
    {
        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        var answer = new RagAnswer(
            Question: "Minha Dúvida Repetida",
            Answer: "Primeira resposta gerada.",
            Sources: []);

        var path1 = await ragEngine.SaveAnswerAsNoteAsync(answer, _tempVaultDir);
        var path2 = await ragEngine.SaveAnswerAsNoteAsync(answer, _tempVaultDir);

        path1.ShouldBe("Brain/Conversas/Minha Dúvida Repetida.md");
        path2.ShouldBe("Brain/Conversas/Minha Dúvida Repetida (1).md");

        File.Exists(Path.Combine(_tempVaultDir, path1)).ShouldBeTrue();
        File.Exists(Path.Combine(_tempVaultDir, path2)).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAnswerAsNoteAsync_WhenLongQuestionWithSpecialChars_ShouldSanitizeAndTruncate()
    {
        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        var longQuestion = "Qual é a melhor forma de organizar um cofre Obsidian gigante com mais de dez mil notas: tags ou pastas? / \\ * < > | ?";
        var answer = new RagAnswer(longQuestion, "Recomenda-se uma estrutura híbrida com MOCs.", []);

        var relativePath = await ragEngine.SaveAnswerAsNoteAsync(answer, _tempVaultDir);

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);
        fileNameWithoutExt.Length.ShouldBeLessThanOrEqualTo(80);
        fileNameWithoutExt.ShouldNotContain(":");
        fileNameWithoutExt.ShouldNotContain("/");
        fileNameWithoutExt.ShouldNotContain("\\");
        fileNameWithoutExt.ShouldNotContain("?");

        var fullPath = Path.Combine(_tempVaultDir, relativePath);
        File.Exists(fullPath).ShouldBeTrue();
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenShouldCapture_ShouldWriteStructuredNoteAndIndexItAndReturnOutcome()
    {
        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.8f, 0.6f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessChatTurnAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<SemanticSearchResult>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatTurnResult
            {
                ShouldCapture = true,
                Title = "Demanda do Chefe — 2026-08-29 12h",
                Category = "Tarefas",
                Tags = ["trabalho", "demanda"],
                BodyMarkdown = "Alinhar demandas com o chefe no almoço de amanhã.",
                KeyPoints = ["Almoço 12h", "Demandas de Q3"],
                SuggestedConnections = [],
                ShouldAnswer = false,
                ReplyMessage = "Anotado! Salvei a demanda com o chefe no seu cofre."
            }));

        var config = new BrainConfig { DefaultFolder = "Brain", AutoCategorizeFolders = true };
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, config);

        var outcome = await ragEngine.ProcessChatTurnAsync("falei com meu chefe hoje tenho uma demanda amanha almoco", _tempVaultDir);

        outcome.ReplyMessage.ShouldContain("Anotado!");
        outcome.SavedNotePath.ShouldNotBeNull();
        outcome.SavedNotePath.ShouldBe("Brain/Tarefas/Demanda do Chefe — 2026-08-29 12h.md");
        outcome.Sources.Count.ShouldBe(0);

        var fullPath = Path.Combine(_tempVaultDir, outcome.SavedNotePath!);
        File.Exists(fullPath).ShouldBeTrue();

        var noteContent = await File.ReadAllTextAsync(fullPath);
        noteContent.ShouldContain("titulo: \"Demanda do Chefe — 2026-08-29 12h\"");
        noteContent.ShouldContain("categoria: \"Tarefas\"");
        noteContent.ShouldContain("# Demanda do Chefe — 2026-08-29 12h");
        noteContent.ShouldContain("Alinhar demandas com o chefe no almoço de amanhã.");
        noteContent.ShouldContain("### Pontos-Chave");
        noteContent.ShouldContain("- Almoço 12h");

        // Confirma que a nova nota foi adicionada ao índice em memória e é encontrada em busca subsequente
        var searchResults = await ragEngine.SearchAsync("chefe", _tempVaultDir);
        searchResults.Count.ShouldBeGreaterThan(0);
        searchResults.ShouldContain(r => r.Title == "Demanda do Chefe — 2026-08-29 12h");
    }

    [Fact]
    public async Task ProcessChatTurnAsync_WhenShouldAnswerOnly_ShouldReturnAnswerAndSourcesWithoutCreatingFile()
    {
        var notePath = Path.Combine(_tempVaultDir, "Demanda.md");
        await File.WriteAllTextAsync(notePath, "Demanda com o chefe agendada para 12h.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 1f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessChatTurnAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<IReadOnlyList<SemanticSearchResult>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatTurnResult
            {
                ShouldCapture = false,
                ShouldAnswer = true,
                ReplyMessage = "Sua demanda é às 12h conforme anotado em [[Demanda]]."
            }));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        var outcome = await ragEngine.ProcessChatTurnAsync("que horas é a demanda?", _tempVaultDir);

        outcome.ReplyMessage.ShouldContain("12h");
        outcome.SavedNotePath.ShouldBeNull();
        outcome.Sources.Count.ShouldBeGreaterThan(0);
        outcome.Sources[0].Title.ShouldBe("Demanda");

        // Garante que nenhum novo arquivo foi criado
        var files = Directory.GetFiles(_tempVaultDir, "*.md", SearchOption.AllDirectories);
        files.Length.ShouldBe(1);
        Path.GetFileName(files[0]).ShouldBe("Demanda.md");
    }

    [Fact]
    public async Task IndexVaultAsync_WhenPersistedIndexExists_LoadsFromDiskAndSkipsEmbeddingCalls()
    {
        var tempIndexDir = Path.Combine(Path.GetTempPath(), $"synapse-idx-shared-{Guid.NewGuid():N}");
        try
        {
            var note1Path = Path.Combine(_tempVaultDir, "Nota1.md");
            var note2Path = Path.Combine(_tempVaultDir, "Nota2.md");
            await File.WriteAllTextAsync(note1Path, "Conteúdo da primeira nota.");
            await File.WriteAllTextAsync(note2Path, "Conteúdo da segunda nota.");

            var store1 = new FileVaultIndexStore(tempIndexDir);
            var mockEmbedding1 = Substitute.For<IEmbeddingProvider>();
            mockEmbedding1.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new float[] { 0.1f, 0.2f }));

            var mockAi = Substitute.For<IBrainAiProvider>();

            var engine1 = new VaultRagEngine(mockEmbedding1, mockAi, indexStore: store1);
            await engine1.IndexVaultAsync(_tempVaultDir);

            await mockEmbedding1.Received(2).GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

            // Segunda instância (ex.: ChatVaultWindow ou reinício do app) usando o mesmo index store
            var store2 = new FileVaultIndexStore(tempIndexDir);
            var mockEmbedding2 = Substitute.For<IEmbeddingProvider>();
            var engine2 = new VaultRagEngine(mockEmbedding2, mockAi, indexStore: store2);

            await engine2.IndexVaultAsync(_tempVaultDir);

            // Nenhuma chamada nova de embedding deve ser feita pois as notas já estão salvas e com mesmo hash
            await mockEmbedding2.DidNotReceiveWithAnyArgs().GenerateEmbeddingAsync(default!, default);

            var searchResults = await engine2.SearchAsync("primeira", _tempVaultDir);
            searchResults.Count.ShouldBeGreaterThan(0);
            searchResults[0].Title.ShouldBe("Nota1");
        }
        finally
        {
            if (Directory.Exists(tempIndexDir))
            {
                try { Directory.Delete(tempIndexDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task IndexVaultAsync_WhenOneNoteModified_OnlyReindexesModifiedNote()
    {
        var tempIndexDir = Path.Combine(Path.GetTempPath(), $"synapse-idx-diff-{Guid.NewGuid():N}");
        try
        {
            var noteAPath = Path.Combine(_tempVaultDir, "NotaA.md");
            var noteBPath = Path.Combine(_tempVaultDir, "NotaB.md");
            await File.WriteAllTextAsync(noteAPath, "Conteúdo original da Nota A.");
            await File.WriteAllTextAsync(noteBPath, "Conteúdo original da Nota B.");

            var store = new FileVaultIndexStore(tempIndexDir);
            var mockEmbedding1 = Substitute.For<IEmbeddingProvider>();
            mockEmbedding1.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new float[] { 0.1f, 0.2f }));

            var mockAi = Substitute.For<IBrainAiProvider>();
            var engine1 = new VaultRagEngine(mockEmbedding1, mockAi, indexStore: store);
            await engine1.IndexVaultAsync(_tempVaultDir);

            // Modifica apenas a Nota B
            await File.WriteAllTextAsync(noteBPath, "Conteúdo MODIFICADO da Nota B com novas informações.");

            var mockEmbedding2 = Substitute.For<IEmbeddingProvider>();
            mockEmbedding2.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new float[] { 0.3f, 0.4f }));

            var engine2 = new VaultRagEngine(mockEmbedding2, mockAi, indexStore: store);
            await engine2.IndexVaultAsync(_tempVaultDir);

            // Apenas a Nota B deve ser enviada para gerar embedding
            await mockEmbedding2.Received(1).GenerateEmbeddingAsync(
                Arg.Is<string>(s => s.Contains("MODIFICADO")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(tempIndexDir))
            {
                try { Directory.Delete(tempIndexDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task SearchAsync_HybridSearch_ShouldRankExactKeywordMatchOverWeakSemanticNeighbor()
    {
        var rustPath = Path.Combine(_tempVaultDir, "Rust.md");
        var golangPath = Path.Combine(_tempVaultDir, "Golang.md");

        await File.WriteAllTextAsync(rustPath, "Guia rápido de instalação e sintaxe da linguagem Rust com cargo e rustc.");
        await File.WriteAllTextAsync(golangPath, "Guia rápido de sintaxe da linguagem Go com goroutines e channels concorrentes.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        // Vetor de consulta para "Rust"
        mockEmbedding.GenerateEmbeddingAsync("Rust", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.5f, 0.5f, 0f }));

        // Nota Golang tem vetor com similaridade 1.0 (ligeiramente superior na matemática pura de embedding)
        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Golang") || s.Contains("goroutines")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.5f, 0.5f, 0f }));

        // Nota Rust tem vetor com similaridade 0.999 (ligeiramente inferior)
        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Rust") || s.Contains("cargo")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.51f, 0.49f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        var results = await ragEngine.SearchAsync("Rust", _tempVaultDir, topK: 2);

        // Graças à busca híbrida com RRF e overlap de palavras-chave no título/conteúdo, Rust.md deve vencer Golang.md
        results.Count.ShouldBe(2);
        results[0].Title.ShouldBe("Rust");
        results[1].Title.ShouldBe("Golang");
        results[0].SimilarityScore.ShouldBeGreaterThan(results[1].SimilarityScore);
    }

    [Fact]
    public async Task SearchAsync_PathAlignment_KeysMatchExactlyForRootAndSubfolders()
    {
        var rootFile = Path.Combine(_tempVaultDir, "NotaRaiz.md");
        var subDir = Path.Combine(_tempVaultDir, "Projetos", "Arquitetura");
        Directory.CreateDirectory(subDir);
        var subFile = Path.Combine(subDir, "PadraoHexagonal.md");

        await File.WriteAllTextAsync(rootFile, "Conteúdo da nota raiz.");
        await File.WriteAllTextAsync(subFile, "Conteúdo da nota na subpasta sobre Hexagonal.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        await ragEngine.IndexVaultAsync(_tempVaultDir);

        var canonicalRoot = HybridSearchEngine.ToCanonicalRelativePath(rootFile, _tempVaultDir);
        var canonicalSub = HybridSearchEngine.ToCanonicalRelativePath(subFile, _tempVaultDir);

        canonicalRoot.ShouldBe("NotaRaiz.md");
        canonicalSub.ShouldBe("Projetos/Arquitetura/PadraoHexagonal.md");

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        mockHybridSearch.SearchAsync("Hexagonal", Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new HybridSearchResult(canonicalSub, 0.05, SearchMatchSource.Both, "snippet", [])
            ]));

        ragEngine.HybridSearchService = mockHybridSearch;

        var results = await ragEngine.SearchAsync("Hexagonal", _tempVaultDir, topK: 2);

        // Uma nota casou, uma nota volta. Antes esta asserção esperava 2, mas o segundo resultado
        // era "NotaRaiz.md" - uma nota sem nenhuma relação com "Hexagonal", que só entrava porque o
        // motor caía no caminho semântico e pontuava o cofre inteiro. Encher a resposta com nota
        // irrelevante é justamente o ruído que degradava o contexto enviado à IA.
        results.Count.ShouldBe(1);
        results[0].RelativePath.ShouldBe("Projetos/Arquitetura/PadraoHexagonal.md");
        results[0].Title.ShouldBe("PadraoHexagonal");
    }

    [Fact]
    public async Task SearchAsync_WithHybridSearchService_LiteralTermRanksTop()
    {
        var note1 = Path.Combine(_tempVaultDir, "NotaA.md");
        var note2 = Path.Combine(_tempVaultDir, "NotaB.md");

        await File.WriteAllTextAsync(note1, "Nota geral com similaridade semântica razoável.");
        await File.WriteAllTextAsync(note2, "Nota contendo TermoLiteralExclusivo.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync("TermoLiteralExclusivo", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("Nota geral")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("TermoLiteralExclusivo")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.5f, 0.5f }));

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        mockHybridSearch.SearchAsync("TermoLiteralExclusivo", false, 200, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new HybridSearchResult("NotaB.md", 0.05, SearchMatchSource.Both, "TermoLiteralExclusivo", [])
            ]));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: mockHybridSearch);

        var results = await ragEngine.SearchAsync("TermoLiteralExclusivo", _tempVaultDir, topK: 2);

        results[0].Title.ShouldBe("NotaB");
    }

    [Fact]
    public async Task SearchAsync_ConceptualMatchWithoutCommonWords_StillAppearsInResults()
    {
        var noteSemelhante = Path.Combine(_tempVaultDir, "ConceitoSemelhante.md");
        var noteLiteral = Path.Combine(_tempVaultDir, "OutroAssunto.md");

        await File.WriteAllTextAsync(noteSemelhante, "Texto puramente conceitual sem nenhuma palavra em comum.");
        await File.WriteAllTextAsync(noteLiteral, "Texto falando de cachorros e gatos.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync("felinos caninos", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.99f, 0.01f }));

        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("conceitual")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.99f, 0.01f }));

        mockEmbedding.GenerateEmbeddingAsync(Arg.Is<string>(s => s.Contains("cachorros")), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.10f, 0.90f }));

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        mockHybridSearch.SearchAsync("felinos caninos", false, 200, Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new HybridSearchResult("OutroAssunto.md", 0.05, SearchMatchSource.Both, "gatos", [])
            ]));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: mockHybridSearch);

        var results = await ragEngine.SearchAsync("felinos caninos", _tempVaultDir, topK: 2);

        results.Any(r => r.Title == "ConceitoSemelhante").ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_WhenHybridSearchServiceIsNull_PreservesLegacyBehavior()
    {
        var note = Path.Combine(_tempVaultDir, "NotaLegada.md");
        await File.WriteAllTextAsync(note, "Conteúdo da nota legado com TermoLegadoUnico.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: null);

        var results = await ragEngine.SearchAsync("TermoLegadoUnico", _tempVaultDir, topK: 1);

        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("NotaLegada");
    }

    [Fact]
    public async Task SearchAsync_WhenHybridSearchServiceThrows_DegradesGracefullyWithoutThrowing()
    {
        var note = Path.Combine(_tempVaultDir, "NotaResiliente.md");
        await File.WriteAllTextAsync(note, "Conteúdo com TermoResiliente para teste de fallback.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        mockHybridSearch.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ThrowingAsyncEnumerable());

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: mockHybridSearch);

        var results = await ragEngine.SearchAsync("TermoResiliente", _tempVaultDir, topK: 1);

        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("NotaResiliente");
    }

    [Fact]
    public void NoteEmbeddingEntry_TokensArePrecomputedAtConstruction()
    {
        var entry = new NoteEmbeddingEntry(
            "MinhaPasta/Nota.md",
            "hash123",
            [0.1f, 0.2f],
            DateTimeOffset.UtcNow,
            ["palavra1", "palavra2"]);

        entry.TokenSet.ShouldNotBeNull();
        entry.TokenSet.Count.ShouldBe(2);
        entry.TokenSet.Contains("PALAVRA1").ShouldBeTrue();
        entry.TokenSet.Contains("palavra2").ShouldBeTrue();
        entry.TokenSet.Contains("inexistente").ShouldBeFalse();

        entry.TitleTokenSet.ShouldNotBeNull();
        entry.TitleTokenSet.Contains("Nota").ShouldBeTrue();
    }

    [Fact]
    public async Task FileVaultIndexStore_LoadsLegacyFormatAndPopulatesTokenSets()
    {
        var tempIndexDir = Path.Combine(Path.GetTempPath(), $"synapse-idx-compat-{Guid.NewGuid():N}");
        try
        {
            var store = new FileVaultIndexStore(tempIndexDir);
            var entry = new NoteEmbeddingEntry(
                "Docs/Guia.md",
                "hash456",
                [0.5f, 0.5f],
                DateTimeOffset.UtcNow,
                ["guia", "rapido"]);

            var dict = new Dictionary<string, NoteEmbeddingEntry> { ["Docs/Guia.md"] = entry };
            await store.SaveAsync(_tempVaultDir, dict);

            var loaded = await store.LoadAsync(_tempVaultDir);

            loaded.ShouldNotBeNull();
            loaded.ContainsKey("Docs/Guia.md").ShouldBeTrue();

            var loadedEntry = loaded["Docs/Guia.md"];
            loadedEntry.TokenSet.ShouldNotBeNull();
            loadedEntry.TokenSet.Contains("guia").ShouldBeTrue();
            loadedEntry.TokenSet.Contains("GUIA").ShouldBeTrue();
            loadedEntry.TitleTokenSet.ShouldNotBeNull();
            loadedEntry.TitleTokenSet.Contains("Guia").ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(tempIndexDir))
            {
                try { Directory.Delete(tempIndexDir, true); } catch { }
            }
        }
    }

    private static async IAsyncEnumerable<HybridSearchResult> AsAsyncEnumerable(IEnumerable<HybridSearchResult> items)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            yield return item;
        }
    }

    private static async IAsyncEnumerable<HybridSearchResult> ThrowingAsyncEnumerable()
    {
        await Task.Yield();
        throw new InvalidOperationException("Falha simulada no motor de busca hibrido.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SearchAsync_ReadsExcerptsOnlyForTopKResults_NotForEveryNoteInTheVault()
    {
        // Arrange - 40 notas no cofre, mas a busca pede so 3. Ler o disco antes do corte
        // significaria 40 aberturas de arquivo por consulta; num cofre real de centenas de
        // milhares de notas era esse o custo dominante da busca.
        const int totalNotes = 40;
        const int topK = 3;

        for (int i = 0; i < totalNotes; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_tempVaultDir, $"nota_{i}.md"),
                $"Conteudo da nota {i} sobre arquitetura de software.");
        }

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        await ragEngine.IndexVaultAsync(_tempVaultDir);

        // Act
        VaultRagEngine.ExcerptReadCount = 0;
        var results = await ragEngine.SearchAsync("arquitetura", _tempVaultDir, topK: topK);

        // Assert
        results.Count.ShouldBe(topK);
        VaultRagEngine.ExcerptReadCount.ShouldBe(topK);

        // E o excerto dos sobreviventes continua sendo preenchido de verdade
        results.ShouldAllBe(r => r.Excerpt.Length > 0);
    }

    [Fact]
    public async Task ReadExcerpt_WhenTermIsOnLine40InLargeNote_IncludesTermInExcerpt()
    {
        // Arrange: nota grande (> 4000 caracteres) onde o termo específico ("Felipe") está na linha 40 numa tabela
        var notePath = Path.Combine(_tempVaultDir, "ListaDeAmigos.md");
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= 60; i++)
        {
            if (i == 40)
            {
                sb.AppendLine("| Nome | Relacao | Detalhes | Data |");
                sb.AppendLine("| Felipe | Colega | Engenheiro de Software na Synapse | 2026-09-02 |");
            }
            else
            {
                sb.AppendLine($"Linha {i:D2}: Preenchimento longo para garantir que o arquivo ultrapasse quatro mil caracteres no teste do Synapse.");
            }
        }
        var fileContent = sb.ToString();
        fileContent.Length.ShouldBeGreaterThan(4000);
        await File.WriteAllTextAsync(notePath, fileContent);

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);

        await ragEngine.IndexVaultAsync(_tempVaultDir);

        // Act
        var results = await ragEngine.SearchAsync("Felipe", _tempVaultDir, topK: 1);

        // Assert: a tabela com Felipe na linha 40 DEVE estar no Excerpt enviado para a IA
        results.Count.ShouldBe(1);
        results[0].Excerpt.ShouldContain("Felipe");
        results[0].Excerpt.ShouldContain("Engenheiro de Software");
    }

    [Fact]
    public async Task ReadExcerpt_WhenNoteIsSmall_IncludesEntireContent()
    {
        // Arrange: nota pequena (<= 4000 caracteres)
        var notePath = Path.Combine(_tempVaultDir, "Pequena.md");
        var content = "# Lista de Tarefas\n- [ ] Comprar café\n- [x] Implementar despachante\n- [ ] Rodar testes";
        await File.WriteAllTextAsync(notePath, content);

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();
        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi);
        await ragEngine.IndexVaultAsync(_tempVaultDir);

        // Act
        var results = await ragEngine.SearchAsync("café", _tempVaultDir, topK: 1);

        // Assert: nota pequena vai inteira sem cortes
        results.Count.ShouldBe(1);
        results[0].Excerpt.ShouldBe(content);
    }

    [Fact]
    public void ReadExcerpt_StripsHtmlTagsFromSnippetAndExcerpts()
    {
        // Arrange
        var rawWithHtml = "Texto com marcação <b>negrito</b> e <i>itálico</i> além de <mark>destaque</mark>.";

        // Act
        var cleaned = VaultRagEngine.StripHtml(rawWithHtml);

        // Assert: nenhuma tag HTML deve permanecer
        cleaned.ShouldBe("Texto com marcação negrito e itálico além de destaque.");
        cleaned.ShouldNotContain("<b>");
        cleaned.ShouldNotContain("</b>");
        cleaned.ShouldNotContain("<mark>");
        cleaned.ShouldNotContain("</mark>");
    }

    [Fact]
    public void EnforceGlobalContextBudget_Respects16kCeilingAndNeverCutsTableLines()
    {
        // Arrange: 4 notas, cada uma com 5.000 caracteres e tabelas Markdown estruturadas
        var notes = new List<SemanticSearchResult>();
        for (int n = 1; n <= 4; n++)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Nota {n} Relevante");
            for (int r = 1; r <= 80; r++)
            {
                sb.AppendLine($"| ColunaA_{r:D2} | ColunaB_{r:D2} | Detalhes bem longos da tabela para preencher tamanho {r:D2} |");
            }
            notes.Add(new SemanticSearchResult($"nota_{n}.md", $"Nota {n}", sb.ToString(), 1.0f / n));
        }

        // Act: aplica o teto global de 16.000 chars
        var budgeted = VaultRagEngine.EnforceGlobalContextBudget(notes, maxChars: 16000);

        // Assert:
        // 1. Teto global respeitado estritamente
        int totalChars = budgeted.Sum(n => n.Excerpt.Length);
        totalChars.ShouldBeLessThanOrEqualTo(16000);

        // 2. Notas mais relevantes são preservadas integralmente primeiro
        budgeted.Count.ShouldBeGreaterThan(0);
        budgeted[0].Title.ShouldBe("Nota 1");

        // 3. NUNCA corta no meio de uma linha de tabela Markdown (| ... |)
        foreach (var note in budgeted)
        {
            var lines = note.Excerpt.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("|"))
                {
                    line.EndsWith("|").ShouldBeTrue("Linha de tabela cortada pela metade!");
                }
            }
        }
    }

    [Fact]
    public async Task SearchAsync_WhenFtsHasSufficientResults_DoesNotCallEmbeddingProviderAtAll()
    {
        // Arrange: 3 notas no cofre com FTS5 devolvendo resultados suficientes (>= 3)
        var note1 = Path.Combine(_tempVaultDir, "Doc1.md");
        var note2 = Path.Combine(_tempVaultDir, "Doc2.md");
        var note3 = Path.Combine(_tempVaultDir, "Doc3.md");

        await File.WriteAllTextAsync(note1, "Conteudo doc1 com termoComum");
        await File.WriteAllTextAsync(note2, "Conteudo doc2 com termoComum");
        await File.WriteAllTextAsync(note3, "Conteudo doc3 com termoComum");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        var mockAi = Substitute.For<IBrainAiProvider>();

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        mockHybridSearch.SearchAsync("termoComum", false, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable([
                new HybridSearchResult("Doc1.md", 0.05, SearchMatchSource.IndexOnly, "snippet 1", []),
                new HybridSearchResult("Doc2.md", 0.04, SearchMatchSource.IndexOnly, "snippet 2", []),
                new HybridSearchResult("Doc3.md", 0.03, SearchMatchSource.IndexOnly, "snippet 3", [])
            ]));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: mockHybridSearch);

        // Act
        var results = await ragEngine.SearchAsync("termoComum", _tempVaultDir, topK: 3);

        // Assert:
        results.Count.ShouldBe(3);

        // PROVA MANDATÓRIA: com FTS5 suficiente (>= 3), IEmbeddingProvider recebe ZERO chamadas!
        _ = mockEmbedding.DidNotReceiveWithAnyArgs().GenerateEmbeddingAsync(default!, default!);
    }

    [Fact]
    public async Task SearchAsync_WhenFtsHasFewerThanThreeResults_CallsEmbeddingProviderAsSafetyNet()
    {
        // Arrange: cofre com notas conceituais, mas FTS5 devolve 0 resultados
        var note1 = Path.Combine(_tempVaultDir, "Filosofia.md");
        await File.WriteAllTextAsync(note1, "Reflexões sobre epistemologia e consciência.");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        // FTS5 devolve 0 resultados (vazio)
        mockHybridSearch.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(Array.Empty<HybridSearchResult>()));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: mockHybridSearch);
        await ragEngine.IndexVaultAsync(_tempVaultDir);

        // Act
        var results = await ragEngine.SearchAsync("teoria do conhecimento", _tempVaultDir, topK: 1);

        // Assert:
        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("Filosofia");

        // PROVA MANDATÓRIA: só quando o FTS5 não devolve NADA a rede de segurança semântica entra.
        _ = mockEmbedding.Received().GenerateEmbeddingAsync("teoria do conhecimento", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WhenFtsFindsFewButExactNotes_DoesNotFallBackToEmbedding()
    {
        // Verificado no app real: "qual minha lista de amigos?" achava exatamente as 2 notas certas,
        // mas o limiar de "suficiente" era >= 3, entao uma busca PRECISA era tratada como fracasso.
        // O motor caia no caminho semantico, pagava o carregamento do modelo de embedding e ainda
        // trazia de volta os registros de atividade do Synapse como notas consultadas.
        var nota1 = Path.Combine(_tempVaultDir, "Lista de Amigos.md");
        var nota2 = Path.Combine(_tempVaultDir, "Lista de Amigos (1).md");
        await File.WriteAllTextAsync(nota1, "| Nome | Relacao |\n| Maria | Namorada |");
        await File.WriteAllTextAsync(nota2, "| Nome | Relacao |\n| Felipe | Amigo |");

        var mockEmbedding = Substitute.For<IEmbeddingProvider>();
        mockEmbedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 1f, 0f }));

        var mockAi = Substitute.For<IBrainAiProvider>();

        var mockHybridSearch = Substitute.For<IHybridSearchService>();
        mockHybridSearch.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsyncEnumerable(
            [
                new HybridSearchResult("Lista de Amigos.md", 0.9, SearchMatchSource.IndexOnly, null, []),
                new HybridSearchResult("Lista de Amigos (1).md", 0.8, SearchMatchSource.IndexOnly, null, [])
            ]));

        var ragEngine = new VaultRagEngine(mockEmbedding, mockAi, hybridSearchService: mockHybridSearch);

        var results = await ragEngine.SearchAsync("qual minha lista de amigos?", _tempVaultDir, topK: 4);

        // As duas notas certas voltam...
        results.Count.ShouldBe(2);

        // ...e o embedding nunca foi chamado: dois resultados exatos ja sao resposta.
        _ = mockEmbedding.DidNotReceive().GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // E o conteudo de AMBAS chega para a IA, nao so o da primeira.
        results.ShouldContain(r => r.Excerpt.Contains("Maria"));
        results.ShouldContain(r => r.Excerpt.Contains("Felipe"));
    }
}
