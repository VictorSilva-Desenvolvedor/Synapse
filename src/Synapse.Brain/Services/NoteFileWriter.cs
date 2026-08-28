using System.Text;
using Synapse.Brain.Models;

namespace Synapse.Brain.Services;

/// <summary>
/// Utilitário compartilhado para formatação de frontmatter, sanitização e gravação de notas estruturadas no cofre.
/// </summary>
public static class NoteFileWriter
{
    public static async Task<string> WriteStructuredNoteAsync(
        AiStructuredNote structured,
        string vaultRootPath,
        BrainConfig config,
        IReadOnlyList<string>? existingNotes = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(structured);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);
        ArgumentNullException.ThrowIfNull(config);

        var body = structured.BodyMarkdown ?? string.Empty;
        body = SanitizeBodyMarkdown(body);
        if (config.EnableAutoLinking)
        {
            if (existingNotes != null && existingNotes.Count > 0)
            {
                body = AutoLinkerService.LinkExistingNotes(body, existingNotes);
            }
            body = AutoLinkerService.AppendConnectionsSection(body, structured.SuggestedConnections);
        }

        var fullNoteMarkdown = BuildFrontmatterNote(structured, body);

        var sanitizedTitle = SanitizeFileName(structured.Title);
        var targetSubFolder = config.DefaultFolder;
        if (config.AutoCategorizeFolders && !string.IsNullOrWhiteSpace(structured.Category))
        {
            targetSubFolder = Path.Combine(config.DefaultFolder, SanitizeFileName(structured.Category));
        }

        var targetDir = Path.Combine(vaultRootPath, targetSubFolder);
        Directory.CreateDirectory(targetDir);

        var targetFilePath = Path.Combine(targetDir, $"{sanitizedTitle}.md");

        var count = 1;
        while (File.Exists(targetFilePath))
        {
            targetFilePath = Path.Combine(targetDir, $"{sanitizedTitle} ({count++}).md");
        }

        await File.WriteAllTextAsync(targetFilePath, fullNoteMarkdown, Encoding.UTF8, ct);

        return Path.GetRelativePath(vaultRootPath, targetFilePath).Replace('\\', '/');
    }

    public static string BuildFrontmatterNote(AiStructuredNote structured, string body)
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

        var trimmedBody = body.Trim();
        if (trimmedBody.StartsWith($"# {structured.Title}", StringComparison.OrdinalIgnoreCase))
        {
            trimmedBody = trimmedBody[$"# {structured.Title}".Length..].TrimStart('\r', '\n');
        }

        sb.AppendLine(trimmedBody);

        return sb.ToString();
    }

    public static IReadOnlyList<string> GetVaultNoteTitles(string vaultRootPath)
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

    public static IReadOnlyList<string> GetExistingCategoryFolders(string vaultRootPath, string defaultFolder)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath)) return [];

        try
        {
            var defaultFolderPath = Path.Combine(vaultRootPath, defaultFolder);
            if (!Directory.Exists(defaultFolderPath)) return [];

            return Directory.GetDirectories(defaultFolderPath)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith('.'))
                .Select(n => n!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Where(c => !invalid.Contains(c))).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Nota" : sanitized;
    }

    public static string SanitizeBodyMarkdown(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var lines = body.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var cleanLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("--- INÍCIO DA NOTA", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("--- FIM DA NOTA", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Você é o assistente", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Você é o arquiteto", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Com base EXCLUSIVAMENTE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("SEMPRE mencione as notas", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Notas do cofre relevantes:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Pergunta do usuário:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("### Fontes Consultadas", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cleanLines.Add(rawLine);
        }

        return string.Join("\n", cleanLines).Trim();
    }
}
