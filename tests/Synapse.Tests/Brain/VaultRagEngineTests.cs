using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

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

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
