using System.Globalization;
using System.Text;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Services;

/// <summary>
/// Gerador de Sínteses Periódicas do Conhecimento (Weekly / Monthly Digest) e Detector de Órfãs (V5.3).
/// </summary>
public sealed class KnowledgeDigestService
{
    private readonly IBrainAiProvider _aiProvider;
    private readonly BrainConfig _config;

    public KnowledgeDigestService(IBrainAiProvider aiProvider, BrainConfig config)
    {
        _aiProvider = aiProvider;
        _config = config;
    }

    public async Task<string> GenerateWeeklyDigestAsync(
        string vaultRootPath,
        DateTimeOffset referenceDate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath) || !Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Cofre não encontrado em: {vaultRootPath}");
        }

        var startDate = referenceDate.AddDays(-7);
        var allNotes = Directory.GetFiles(vaultRootPath, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".obsidian") && !f.Contains("_conflitos") && !f.Contains(".trash") && !f.Contains("Digests"))
            .ToList();

        var recentNotes = new List<(string RelativePath, string Title, string Content)>();
        var orphanNotes = new List<string>();

        foreach (var file in allNotes)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                var content = await File.ReadAllTextAsync(file, ct);
                var relativePath = Path.GetRelativePath(vaultRootPath, file).Replace('\\', '/');
                var title = Path.GetFileNameWithoutExtension(file);

                if (fileInfo.LastWriteTimeUtc >= startDate.UtcDateTime)
                {
                    recentNotes.Add((relativePath, title, content));
                }

                // Detecta notas órfãs (sem nenhum [[wikilink]])
                if (!content.Contains("[[") && !content.Contains("]]"))
                {
                    orphanNotes.Add(title);
                }
            }
            catch { }
        }

        var weekNum = ISOWeek.GetWeekOfYear(referenceDate.DateTime);
        var digestTitle = $"Síntese Semanal — Semana {weekNum} ({referenceDate:yyyy})";

        var prompt = $@"Você é o curador de conhecimento do Segundo Cérebro do Obsidian.
Gere um relatório de síntese semanal com base nas {recentNotes.Count} notas criadas/editadas recentemente:
- Resumo executivo dos temas explorados
- Destaques e principais aprendizados
- Lista estruturada com wikilinks [[Nome da Nota]] para cada nota recente
- Sugestão de conexões para as notas que estão isoladas/órfãs: [{string.Join(", ", orphanNotes.Take(10))}]

Notas recentes:
" + string.Join("\n\n", recentNotes.Take(15).Select(n => $"--- NOTA: [[{n.Title}]] ---\n" + (n.Content.Length > 800 ? n.Content[..800] + "..." : n.Content)));

        var aiResponse = await _aiProvider.ProcessRawNoteAsync(prompt, allNotes.Select(n => Path.GetFileNameWithoutExtension(n)).ToList(), ct);

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"titulo: \"{digestTitle}\"");
        sb.AppendLine("categoria: \"Digest\"");
        sb.AppendLine($"criado_em: \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
        sb.AppendLine("tags:");
        sb.AppendLine("  - digest");
        sb.AppendLine("  - sintese-semanal");
        sb.AppendLine("  - segundo-cerebro");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# 📑 {digestTitle}");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(aiResponse.BodyMarkdown) ? aiResponse.Summary : aiResponse.BodyMarkdown);
        sb.AppendLine();

        if (recentNotes.Count > 0)
        {
            sb.AppendLine("## 📌 Notas Trabalhadas no Período");
            foreach (var note in recentNotes)
            {
                sb.AppendLine($"- [[{note.Title}]] (`{note.RelativePath}`)");
            }
            sb.AppendLine();
        }

        if (orphanNotes.Count > 0)
        {
            sb.AppendLine("## 💡 Notas Órfãs (Oportunidades de Conexão)");
            foreach (var orphan in orphanNotes.Take(8))
            {
                sb.AppendLine($"- [[{orphan}]]");
            }
        }

        var digestsDir = Path.Combine(vaultRootPath, _config.DefaultFolder, "Digests");
        Directory.CreateDirectory(digestsDir);

        var targetFile = Path.Combine(digestsDir, $"Digest-{referenceDate:yyyy}-W{weekNum:D2}.md");
        await File.WriteAllTextAsync(targetFile, sb.ToString(), Encoding.UTF8, ct);

        return Path.GetRelativePath(vaultRootPath, targetFile).Replace('\\', '/');
    }
}
