using System.Text.RegularExpressions;
using Synapse.Brain.Models;

namespace Synapse.Brain.Services;

/// <summary>
/// Serviço de captura e estruturação de artigos e páginas da web para o cofre (V5.4).
/// </summary>
public sealed class WebClipperService
{
    private readonly SmartCaptureService _captureService;

    public WebClipperService(SmartCaptureService captureService)
    {
        _captureService = captureService;
    }

    public async Task<string> ClipWebPageAsync(
        string url,
        string pageTitle,
        string rawHtmlOrText,
        string vaultRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);

        var cleanText = SanitizeHtml(rawHtmlOrText);
        if (cleanText.Length > 15000)
        {
            cleanText = cleanText[..15000] + "\n\n[... Artigo truncado para síntese ...]";
        }

        var prompt = $@"Você é o Web Clipper do Segundo Cérebro do Obsidian.
Analise a página/artigo capturado da web e estruture como uma nota de referência completa:
- Título conciso do artigo
- Categoria: Referencia
- Tags temáticas relevantes
- Resumo executivo dos pontos principais
- Seção 'Destaques e Lições'
- URL de origem: {url}

Conteúdo capturado:
---
Título original: {pageTitle}
URL: {url}

{cleanText}
---";

        return await _captureService.ProcessAndSaveToVaultAsync(prompt, vaultRootPath, ct);
    }

    private static string SanitizeHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        // Remove scripts e styles
        var noScripts = Regex.Replace(input, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
        var noStyles = Regex.Replace(noScripts, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

        // Remove tags HTML substituindo por espaços
        var text = Regex.Replace(noStyles, @"<[^>]+>", " ", RegexOptions.Multiline);

        // Decodifica entidades HTML e limpa espaços múltiplos
        var decoded = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(decoded, @"\s{2,}", " ").Trim();
    }
}
