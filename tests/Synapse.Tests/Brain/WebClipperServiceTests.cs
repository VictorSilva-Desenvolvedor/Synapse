using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class WebClipperServiceTests : IDisposable
{
    private readonly string _tempVaultDir;

    public WebClipperServiceTests()
    {
        _tempVaultDir = Path.Combine(Path.GetTempPath(), $"synapse-clipper-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVaultDir);
    }

    [Fact]
    public async Task ClipWebPageAsync_ShouldSanitizeHtmlAndSaveStructuredNote()
    {
        var rawHtml = "<html><head><script>alert('xss')</script><style>.ad{color:red}</style></head><body><h1>Artigo de IA</h1><p>Texto do artigo sobre modelos fundacionais.</p></body></html>";

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessRawNoteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiStructuredNote
            {
                Title = "Artigo de IA",
                Category = "Referencia",
                BodyMarkdown = "Resumo do artigo sobre modelos fundacionais."
            }));

        var config = new BrainConfig { DefaultFolder = "Brain" };
        var smartCapture = new SmartCaptureService(mockAi, config);
        var clipperService = new WebClipperService(smartCapture);

        var relativePath = await clipperService.ClipWebPageAsync(
            "https://exemplo.com/artigo-ia",
            "Artigo de IA",
            rawHtml,
            _tempVaultDir);

        relativePath.ShouldContain("Artigo de IA.md");
        File.Exists(Path.Combine(_tempVaultDir, relativePath)).ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempVaultDir))
        {
            try { Directory.Delete(_tempVaultDir, true); } catch { }
        }
    }
}
