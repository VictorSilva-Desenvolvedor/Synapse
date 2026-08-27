using System.Text;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Services;

/// <summary>
/// Orquestrador de Captura Inteligente e Estruturação do Segundo Cérebro.
/// </summary>
public sealed class SmartCaptureService
{
    private readonly IBrainAiProvider _aiProvider;
    private readonly BrainConfig _config;

    public SmartCaptureService(IBrainAiProvider aiProvider, BrainConfig config)
    {
        _aiProvider = aiProvider;
        _config = config;
    }

    public async Task<string> ProcessAndSaveToVaultAsync(
        string rawInput,
        string vaultRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);

        // 1. Escaneia notas existentes no cofre para contextualização da IA
        var existingNotes = GetVaultNoteTitles(vaultRootPath);

        // 2. Processa com o provedor de IA
        var structured = await _aiProvider.ProcessRawNoteAsync(rawInput, existingNotes, ct);

        // 3. Aplica Auto-Linking se habilitado
        var body = structured.BodyMarkdown;
        if (_config.EnableAutoLinking)
        {
            body = AutoLinkerService.LinkExistingNotes(body, existingNotes);
            body = AutoLinkerService.AppendConnectionsSection(body, structured.SuggestedConnections);
        }

        // 4. Monta Frontmatter YAML e Conteúdo Final
        var fullNoteMarkdown = BuildFrontmatterNote(structured, body);

        // 5. Determina pasta de destino e nome do arquivo
        var sanitizedTitle = SanitizeFileName(structured.Title);
        var targetSubFolder = _config.DefaultFolder;
        if (_config.AutoCategorizeFolders && !string.IsNullOrWhiteSpace(structured.Category))
        {
            targetSubFolder = Path.Combine(_config.DefaultFolder, SanitizeFileName(structured.Category));
        }

        var targetDir = Path.Combine(vaultRootPath, targetSubFolder);
        Directory.CreateDirectory(targetDir);

        var targetFilePath = Path.Combine(targetDir, $"{sanitizedTitle}.md");

        // Se já existir, anexa sufixo numérico
        var count = 1;
        while (File.Exists(targetFilePath))
        {
            targetFilePath = Path.Combine(targetDir, $"{sanitizedTitle} ({count++}).md");
        }

        // 6. Grava no cofre local
        await File.WriteAllTextAsync(targetFilePath, fullNoteMarkdown, Encoding.UTF8, ct);

        return Path.GetRelativePath(vaultRootPath, targetFilePath).Replace('\\', '/');
    }

    private static string BuildFrontmatterNote(AiStructuredNote structured, string body)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"titulo: \"{structured.Title.Replace("\"", "\\\"")}\"");
        sb.AppendLine($"categoria: \"{structured.Category}\"");
        sb.AppendLine($"criado_em: \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
        sb.AppendLine("status: processado");
        if (!string.IsNullOrWhiteSpace(structured.Summary))
        {
            sb.AppendLine($"resumo: \"{structured.Summary.Replace("\"", "\\\"")}\"");
        }

        if (structured.Tags.Count > 0)
        {
            sb.AppendLine("tags:");
            foreach (var tag in structured.Tags)
            {
                sb.AppendLine($"  - {tag.TrimStart('#')}");
            }
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {structured.Title}");
        sb.AppendLine();

        if (structured.KeyPoints.Count > 0)
        {
            sb.AppendLine("### Pontos-Chave");
            foreach (var kp in structured.KeyPoints)
            {
                sb.AppendLine($"- {kp}");
            }
            sb.AppendLine();
        }

        sb.AppendLine(body.Trim());

        return sb.ToString();
    }

    private static IReadOnlyList<string> GetVaultNoteTitles(string vaultRootPath)
    {
        if (!Directory.Exists(vaultRootPath)) return [];

        try
        {
            return Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".obsidian") && !f.Contains("_conflitos") && !f.Contains(".trash"))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Where(c => !invalid.Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Nota" : sanitized;
    }
}
