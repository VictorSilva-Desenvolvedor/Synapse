using System.Text.RegularExpressions;

namespace Synapse.Brain.Services;

/// <summary>
/// Motor de auto-linking semântico para conectar novas notas ao grafo do cofre do Obsidian.
/// </summary>
public static class AutoLinkerService
{
    public static string LinkExistingNotes(string markdownContent, IReadOnlyList<string> existingNotes)
    {
        if (string.IsNullOrWhiteSpace(markdownContent) || existingNotes == null || existingNotes.Count == 0)
        {
            return markdownContent;
        }

        var result = markdownContent;

        // Ordena notas por tamanho descrescente para evitar substituições parciais (ex: "Arquitetura Hexagonal" antes de "Arquitetura")
        var sortedNotes = existingNotes
            .Where(n => !string.IsNullOrWhiteSpace(n) && n.Length >= 3)
            .OrderByDescending(n => n.Length)
            .ToList();

        foreach (var note in sortedNotes)
        {
            var noteName = Path.GetFileNameWithoutExtension(note);
            if (string.IsNullOrWhiteSpace(noteName) || noteName.Length < 3) continue;

            // Regex para encontrar menção da palavra exata fora de [[...]] e de markdown links [text](url)
            var pattern = $@"(?<!\[\[)(?<!\]\])\b({Regex.Escape(noteName)})\b(?!\]\])(?![^\[]*\]\()";

            try
            {
                result = Regex.Replace(result, pattern, $"[[$1]]", RegexOptions.IgnoreCase);
            }
            catch
            {
                // Silencioso em caso de regex inválida em nomes com caracteres especiais
            }
        }

        return result;
    }

    public static string AppendConnectionsSection(string markdownContent, IReadOnlyList<string> connectedNotes)
    {
        if (connectedNotes == null || connectedNotes.Count == 0)
        {
            return markdownContent;
        }

        var distinctNotes = connectedNotes
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => Path.GetFileNameWithoutExtension(n))
            .Distinct()
            .ToList();

        if (distinctNotes.Count == 0) return markdownContent;

        // Se já houver a seção, não duplica
        if (markdownContent.Contains("## Conexões", StringComparison.OrdinalIgnoreCase) ||
            markdownContent.Contains("## Notas Relacionadas", StringComparison.OrdinalIgnoreCase))
        {
            return markdownContent;
        }

        var sb = new System.Text.StringBuilder(markdownContent.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Conexões & Notas Relacionadas");
        foreach (var note in distinctNotes)
        {
            sb.AppendLine($"- [[{note}]]");
        }

        return sb.ToString();
    }
}
