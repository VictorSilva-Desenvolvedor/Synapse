using System.Net;
using NSubstitute;
using Shouldly;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;
using Synapse.Brain.Services;

namespace Synapse.Tests.Brain;

public class AudioVoiceCaptureServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AudioVoiceCaptureServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"synapse-audio-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseContent;

        public MockHttpMessageHandler(string responseContent)
        {
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ProcessAudioFileAndSaveAsync_WhenAudioProvided_ShouldTranscribeAndSave()
    {
        var dummyAudioPath = Path.Combine(_tempDir, "gravacao.mp3");
        await File.WriteAllBytesAsync(dummyAudioPath, [0x49, 0x44, 0x33, 0x00]); // Fake MP3 header

        var geminiResponse = @"{
  ""candidates"": [
    {
      ""content"": {
        ""parts"": [
          {
            ""text"": ""# Ideia de Projeto\n\nGravação sobre criar um aplicativo de notas inteligentes.""
          }
        ]
      }
    }
  ]
}";

        var handler = new MockHttpMessageHandler(geminiResponse);
        var httpClient = new HttpClient(handler);

        var config = new BrainConfig
        {
            GeminiApiKey = "AIzaSyTestKey",
            DefaultFolder = "Brain"
        };

        var mockAi = Substitute.For<IBrainAiProvider>();
        mockAi.ProcessRawNoteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiStructuredNote
            {
                Title = "Ideia de Projeto",
                Category = "Ideia",
                BodyMarkdown = "Gravação sobre criar um aplicativo de notas inteligentes."
            }));

        var smartCapture = new SmartCaptureService(mockAi, config);
        var audioService = new AudioVoiceCaptureService(config, smartCapture, httpClient);

        var relativePath = await audioService.ProcessAudioFileAndSaveAsync(dummyAudioPath, _tempDir);

        relativePath.ShouldContain("Ideia de Projeto.md");
        File.Exists(Path.Combine(_tempDir, relativePath)).ShouldBeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
