using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;
using Synapse.Conflict;
using Synapse.Core.Ports;
using Synapse.Host.Http;
using Synapse.Rules;
using Synapse.Sync;
using Synapse.Sync.Auth;
using Synapse.Sync.Config;
using Synapse.Sync.GitHub;

namespace Synapse.Tests.E2E;

/// <summary>
/// Suíte de Validação Real Ponta a Ponta contra serviços reais, Named Pipe, HTTP, DPAPI e GitHub REST API.
/// </summary>
[Collection("RealE2E")]
public class RealE2EVerificationTests : IDisposable
{
    private readonly string _tempVaultDir;
    private readonly string? _gitHubToken;
    private const string TestOwner = "VictorSilva-Desenvolvedor";
    private const string TestRepo = "synapse-e2e-test-vault";

    public RealE2EVerificationTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-real-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);

        _gitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(_gitHubToken))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    _gitHubToken = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                }
            }
            catch { }
        }
    }

    [Fact]
    public async Task E2E_Scenario1_LocalToGitHub_RealUploadAndVerification()
    {
        if (string.IsNullOrWhiteSpace(_gitHubToken)) return;

        var tokenStore = new DpapiTokenStore();
        await tokenStore.SaveTokenAsync(new GitHubToken(_gitHubToken, "VictorSilva-Desenvolvedor"));

        var clientConfig = new GitHubClientConfig
        {
            Owner = TestOwner,
            Repository = TestRepo,
            Branch = "main"
        };

        var authManager = new GitHubAuthManager(tokenStore, clientConfig);
        var gitHubProvider = new GitHubProvider(authManager, clientConfig);

        // 1. Cria nota local
        var noteRelPath = "Notas/PrimeiraNota.md";
        var noteFullPath = Path.Combine(_tempVaultDir, noteRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(noteFullPath)!);
        var expectedContent = $"# Primeira Nota E2E\nCriada em {DateTime.UtcNow:O} no teste real.";
        await File.WriteAllTextAsync(noteFullPath, expectedContent);

        // 2. Upload real para o repositório no GitHub via GitHubProvider do Synapse
        var cloudFile = await gitHubProvider.UploadAsync(noteFullPath, "Notas", CancellationToken.None);

        cloudFile.ShouldNotBeNull();
        cloudFile.Name.ShouldBe("PrimeiraNota.md");

        // 3. Verificação direta via chamada HTTP à API pública do GitHub
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Synapse-E2E-Verifier", "1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _gitHubToken);

        var response = await client.GetAsync($"https://api.github.com/repos/{TestOwner}/{TestRepo}/contents/{noteRelPath}");
        response.IsSuccessStatusCode.ShouldBeTrue();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var base64Content = doc.RootElement.GetProperty("content").GetString()?.Replace("\n", "").Replace("\r", "");
        var decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(base64Content!));

        decodedContent.ShouldBe(expectedContent);
    }

    [Fact]
    public async Task E2E_Scenario2_GitHubToLocal_RealRemotePullAndDownload()
    {
        if (string.IsNullOrWhiteSpace(_gitHubToken)) return;

        var tokenStore = new DpapiTokenStore();
        await tokenStore.SaveTokenAsync(new GitHubToken(_gitHubToken, "VictorSilva-Desenvolvedor"));

        var clientConfig = new GitHubClientConfig
        {
            Owner = TestOwner,
            Repository = TestRepo,
            Branch = "main"
        };

        var authManager = new GitHubAuthManager(tokenStore, clientConfig);
        var gitHubProvider = new GitHubProvider(authManager, clientConfig);

        // 1. Cria arquivo diretamente no repositório remoto via API do GitHub
        var remoteRelPath = "Remoto/NotaCriadaNaNuvem.md";
        var remoteContent = $"# Nota Criada Diretamente no GitHub\nTimestamp: {DateTime.UtcNow:O}";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Synapse-E2E-Verifier", "1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _gitHubToken);

        var existingResp = await client.GetAsync($"https://api.github.com/repos/{TestOwner}/{TestRepo}/contents/{remoteRelPath}");
        string? existingSha = null;
        if (existingResp.IsSuccessStatusCode)
        {
            var existingJson = await existingResp.Content.ReadAsStringAsync();
            using var existingDoc = JsonDocument.Parse(existingJson);
            existingSha = existingDoc.RootElement.GetProperty("sha").GetString();
        }

        var putPayload = new
        {
            message = "test(e2e): criando arquivo remoto para teste de pull",
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(remoteContent)),
            sha = existingSha
        };

        var putResp = await client.PutAsync(
            $"https://api.github.com/repos/{TestOwner}/{TestRepo}/contents/{remoteRelPath}",
            new StringContent(JsonSerializer.Serialize(putPayload), Encoding.UTF8, "application/json"));
        putResp.IsSuccessStatusCode.ShouldBeTrue();

        // 2. Faz download via GitHubProvider do Synapse
        var localDownloadPath = Path.Combine(_tempVaultDir, remoteRelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(localDownloadPath)!);

        await gitHubProvider.DownloadAsync(remoteRelPath, localDownloadPath, CancellationToken.None);

        // 3. Valida conteúdo em disco local
        File.Exists(localDownloadPath).ShouldBeTrue();
        var contentOnDisk = await File.ReadAllTextAsync(localDownloadPath);
        contentOnDisk.ShouldBe(remoteContent);
    }

    [Fact]
    public void E2E_Scenario3_RealConflictResolution_NonOverlappingMerge()
    {
        var merger = new ThreeWayMerger();

        var baseContent = "Linha 1\nLinha 2\nLinha 3\nLinha 4\nLinha 5\n";
        var localContent = "Linha 1 [Modificado Localmente]\nLinha 2\nLinha 3\nLinha 4\nLinha 5\n";
        var remoteContent = "Linha 1\nLinha 2\nLinha 3\nLinha 4\nLinha 5 [Modificado Remotamente]\n";

        var result = merger.Merge(baseContent, localContent, remoteContent);

        result.ShouldBeOfType<MergeResult.Resolved>();
        var resolved = (MergeResult.Resolved)result;
        resolved.MergedContent.ShouldContain("Linha 1 [Modificado Localmente]");
        resolved.MergedContent.ShouldContain("Linha 5 [Modificado Remotamente]");
    }

    [Fact]
    public void E2E_Scenario3_RealConflictResolution_UnresolvableConflictDetected()
    {
        var merger = new ThreeWayMerger();

        var baseContent = "Título Original\nLinha comum\n";
        var localContent = "Título Local Conflitante\nLinha comum\n";
        var remoteContent = "Título Remoto Conflitante\nLinha comum\n";

        var result = merger.Merge(baseContent, localContent, remoteContent);

        result.ShouldBeOfType<MergeResult.Unresolvable>();
        var unresolvable = (MergeResult.Unresolvable)result;
        unresolvable.LocalContent.ShouldBe(localContent);
        unresolvable.RemoteContent.ShouldBe(remoteContent);
    }

    [Fact]
    public async Task E2E_Scenario4_RealWebClipperHttpServer()
    {
        var mockAi = new MockAiProvider();
        var brainConfig = new BrainConfig { DefaultFolder = "Brain" };
        var smartCapture = new SmartCaptureService(mockAi, brainConfig);
        var clipperService = new WebClipperService(smartCapture);
        var configManager = new SynapseConfigManager(Path.Combine(_tempVaultDir, ".synapse", "config.json"));

        await configManager.SaveAsync(new SynapseConfig
        {
            VaultPath = _tempVaultDir,
            Owner = TestOwner,
            Repository = TestRepo
        });

        var testPort = 57425;
        using var httpClipperServer = new LocalHttpClipperServer(clipperService, configManager, port: testPort);
        httpClipperServer.Start();
        httpClipperServer.IsRunning.ShouldBeTrue();

        // Envia requisição HTTP real para http://127.0.0.1:{testPort}/clip
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var clipPayload = new
        {
            url = "https://synapse-pkm.dev/artigo-e2e",
            title = "Artigo de Teste Real",
            content = "<h1>Título do Artigo</h1><p>Conteúdo real capturado pelo clipper.</p>"
        };

        HttpResponseMessage? clipResponse = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                clipResponse = await httpClient.PostAsync(
                    $"http://127.0.0.1:{testPort}/clip",
                    new StringContent(JsonSerializer.Serialize(clipPayload), Encoding.UTF8, "application/json"));
                if (clipResponse.IsSuccessStatusCode) break;
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(100);
            }
        }

        clipResponse.ShouldNotBeNull();
        clipResponse.IsSuccessStatusCode.ShouldBeTrue();
        var clipResultJson = await clipResponse.Content.ReadAsStringAsync();
        clipResultJson.ShouldContain("success\":true");

        // Verifica se a nota foi gravada no cofre
        var createdFiles = Directory.GetFiles(_tempVaultDir, "*.md", SearchOption.AllDirectories);
        createdFiles.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task E2E_Scenario5_RealRulesEngineExecution()
    {
        // 1. Configura regra real no disco
        var rulesDir = Path.Combine(_tempVaultDir, ".synapse");
        Directory.CreateDirectory(rulesDir);
        var rulesYamlPath = Path.Combine(rulesDir, "regras.yaml");

        var yaml = @"
regras:
  - tipo: auto_tag
    pasta_origem: Diario
    tags:
      - diario
      - validacao-real
";
        await File.WriteAllTextAsync(rulesYamlPath, yaml);

        // 2. Cria nota real na pasta Diario
        var dailyDir = Path.Combine(_tempVaultDir, "Diario");
        Directory.CreateDirectory(dailyDir);
        var dailyNote = Path.Combine(dailyDir, "2026-08-27.md");
        await File.WriteAllTextAsync(dailyNote, "# Meu Diário\nHoje realizei testes reais.");

        // 3. Executa RuleEngine e RuleExecutor
        var fileSystem = new LocalFileSystem();
        var ruleEngine = new RuleEngine(fileSystem, _tempVaultDir);
        await ruleEngine.LoadRulesAsync(rulesYamlPath, CancellationToken.None);

        var actions = await ruleEngine.EvaluateAsync(new NoteContext("Diario/2026-08-27.md", "", DateTimeOffset.UtcNow), CancellationToken.None);
        actions.Count.ShouldBe(1);

        var executor = new RuleExecutor(fileSystem, _tempVaultDir);
        await executor.ExecuteActionAsync(actions[0]);

        // 4. Verifica nota modificada com tags aplicadas no frontmatter
        var updatedContent = await File.ReadAllTextAsync(dailyNote);
        updatedContent.ShouldContain("tags:");
        updatedContent.ShouldContain("- diario");
        updatedContent.ShouldContain("- validacao-real");
    }

    [Fact]
    public async Task E2E_Scenario6_RealDpapiTokenEncryptionAndDecryption()
    {
        var tempKeyFile = Path.Combine(_tempVaultDir, "test-token.dat");
        var tokenStore = new DpapiTokenStore(tempKeyFile);

        var sampleToken = "gho_RealTestTokenProtectedByWindowsDPAPI_12345";
        var savedToken = new GitHubToken(sampleToken, "VictorSilva-Desenvolvedor");

        await tokenStore.SaveTokenAsync(savedToken);

        // O arquivo físico existe e NÃO contém o token em texto puro
        File.Exists(tempKeyFile).ShouldBeTrue();
        var rawFileBytes = await File.ReadAllBytesAsync(tempKeyFile);
        var rawFileText = Encoding.UTF8.GetString(rawFileBytes);
        rawFileText.ShouldNotContain(sampleToken);

        // O leitor DPAPI descriptografa corretamente
        var loadedToken = await tokenStore.LoadTokenAsync();
        loadedToken.ShouldNotBeNull();
        loadedToken.Token.ShouldBe(sampleToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }

    private sealed class MockAiProvider : IBrainAiProvider
    {
        public string ProviderName => "MockE2E";

        public Task<AiStructuredNote> ProcessRawNoteAsync(string rawText, IReadOnlyList<string> existingNoteTitles, CancellationToken ct = default)
        {
            return Task.FromResult(new AiStructuredNote
            {
                Title = "Artigo de Teste Real",
                Category = "Referencia",
                Tags = ["artigo", "e2e"],
                Summary = "Resumo do artigo capturado.",
                BodyMarkdown = "# Artigo de Teste Real\n\nConteúdo capturado com sucesso."
            });
        }

        public Task<string> GenerateMocAsync(string topic, IReadOnlyList<string> relatedNotes, CancellationToken ct = default)
        {
            return Task.FromResult($"# MOC: {topic}\n- [[Artigo de Teste Real]]");
        }

        public Task<string> AskQuestionAsync(string prompt, CancellationToken ct = default)
        {
            return Task.FromResult("Resposta de teste E2E.");
        }

        public Task<ChatTurnResult> ProcessChatTurnAsync(
            string userMessage,
            IReadOnlyList<string> existingVaultNotes,
            IReadOnlyList<string> existingCategoryFolders,
            IReadOnlyList<SemanticSearchResult> relatedNotes,
            CancellationToken ct = default)
        {
            return Task.FromResult(new ChatTurnResult
            {
                ShouldCapture = true,
                Title = "Artigo de Teste Real",
                Category = "Referencia",
                Tags = ["artigo", "e2e"],
                BodyMarkdown = "# Artigo de Teste Real\n\nConteúdo capturado com sucesso.",
                ReplyMessage = "Anotado no cofre com sucesso."
            });
        }
    }
}
