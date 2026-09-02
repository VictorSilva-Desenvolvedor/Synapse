using NSubstitute;
using Shouldly;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;
using Xunit;

namespace Synapse.Tests.Brain;

/// <summary>
/// O Tray indexa o cofre em background enquanto o usuario pergunta no chat. As duas coisas
/// batem no mesmo dicionario de vetores do VaultRagEngine: a indexacao escreve, a busca
/// percorre. Percorrer um Dictionary enquanto outra thread escreve nele lanca
/// InvalidOperationException("Collection was modified").
/// </summary>
public class VaultRagEngineConcurrencyTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), $"synapse-rag-conc-{Guid.NewGuid():N}");

    public VaultRagEngineConcurrencyTests() => Directory.CreateDirectory(_vault);

    public void Dispose()
    {
        if (Directory.Exists(_vault))
        {
            Directory.Delete(_vault, recursive: true);
        }
    }

    private static IEmbeddingProvider VetorFalso()
    {
        var e = Substitute.For<IEmbeddingProvider>();
        e.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new float[] { 0.5f, 0.5f, 0.5f }));
        return e;
    }

    [Fact]
    public async Task BuscaDuranteIndexacaoEmBackground_NaoQuebra()
    {
        for (var i = 0; i < 400; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_vault, $"nota-{i}.md"), $"conteudo da nota {i} sobre sincronizacao e cofre");
        }

        var engine = new VaultRagEngine(VetorFalso(), Substitute.For<IBrainAiProvider>());

        // Primeira carga: a busca so percorre o dicionario se ele nao estiver vazio.
        await engine.IndexVaultAsync(_vault);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var indexando = Task.Run(async () =>
        {
            for (var rodada = 0; rodada < 6; rodada++)
            {
                // Notas NOVAS a cada rodada. Isto e o que importa: o Dictionary so invalida o
                // enumerador em mudanca ESTRUTURAL. Reescrever o valor de uma chave existente nao
                // conta - so inserir chave nova (ou remover) conta.
                for (var i = 0; i < 400; i++)
                {
                    await File.WriteAllTextAsync(Path.Combine(_vault, $"r{rodada}-nota-{i}.md"), $"rodada {rodada} nota {i} sobre sincronizacao e cofre", cts.Token);
                }

                await engine.IndexVaultAsync(_vault, cts.Token);
            }
        }, cts.Token);

        var buscando = Task.Run(async () =>
        {
            while (!indexando.IsCompleted)
            {
                await engine.SearchAsync("sincronizacao", _vault, topK: 5, cts.Token);
            }
        }, cts.Token);

        var erro = await Record.ExceptionAsync(() => Task.WhenAll(indexando, buscando));

        erro.ShouldBeNull($"indice compartilhado quebrou: {erro?.GetType().Name}: {erro?.Message}");
    }
}
