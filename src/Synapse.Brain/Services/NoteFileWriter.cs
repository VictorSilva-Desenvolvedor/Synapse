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

        sb.AppendLine(body.Trim());

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
}
