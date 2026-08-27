using System.Text;
using System.Text.Json;
using Synapse.Brain.Models;

namespace Synapse.Brain.Services;

/// <summary>
/// Serviço de Transcrição e Estruturação de Áudio/Voz usando a API Multimodal do Gemini (V5.2).
/// </summary>
public sealed class AudioVoiceCaptureService
{
    private readonly HttpClient _httpClient;
    private readonly BrainConfig _config;
    private readonly SmartCaptureService _captureService;

    public AudioVoiceCaptureService(
        BrainConfig config,
        SmartCaptureService captureService,
        HttpClient? httpClient = null)
    {
        _config = config;
        _captureService = captureService;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> ProcessAudioFileAndSaveAsync(
        string audioFilePath,
        string vaultRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException("Arquivo de áudio não encontrado.", audioFilePath);
        }

        var apiKey = _config.GetEffectiveGeminiApiKey();
        var audioBytes = await File.ReadAllBytesAsync(audioFilePath, ct);
        var base64Audio = Convert.ToBase64String(audioBytes);
        var mimeType = GetMimeType(audioFilePath);

        var prompt = @"Você é o assistente de voz do Synapse para Obsidian.
Ouça o áudio fornecido, faça a transcrição completa do que foi falado, e estruture as ideias principais em Markdown:
- Título conciso
- Transcrição limpa
- Pontos-chave e tarefas
- Conclusão";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Fallback heurístico caso não haja API Key
            var audioName = Path.GetFileNameWithoutExtension(audioFilePath);
            var rawText = $"# Áudio Capturado: {audioName}\n\nGravação de voz recebida em {DateTime.Now:dd/MM/yyyy HH:mm:ss}.\n\n*(Configure sua Gemini API Key para transcrição automática por IA)*";
            return await _captureService.ProcessAndSaveToVaultAsync(rawText, vaultRootPath, ct);
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = base64Audio
                            }
                        }
                    }
                }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_config.GeminiModel}:generateContent?key={apiKey}";

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, ct);

            if (response.IsSuccessStatusCode)
            {
                var jsonStr = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonStr);
                var candidates = doc.RootElement.GetProperty("candidates");
                if (candidates.GetArrayLength() > 0)
                {
                    var transcribedMarkdown = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(transcribedMarkdown))
                    {
                        return await _captureService.ProcessAndSaveToVaultAsync(transcribedMarkdown, vaultRootPath, ct);
                    }
                }
            }
        }
        catch
        {
        }

        // Fallback
        var fallbackTitle = Path.GetFileNameWithoutExtension(audioFilePath);
        return await _captureService.ProcessAndSaveToVaultAsync($"Áudio de Voz: {fallbackTitle}", vaultRootPath, ct);
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mp3",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/m4a",
            _ => "audio/mp3"
        };
    }
}
