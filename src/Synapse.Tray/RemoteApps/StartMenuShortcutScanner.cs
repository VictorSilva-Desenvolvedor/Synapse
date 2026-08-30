using System.Globalization;
using System.IO;
using System.Text;

namespace Synapse.Tray.RemoteApps;

public sealed record DiscoveredApp(string Name, string SuggestedKey, string ShortcutPath);

/// <summary>
/// Varredor estático e seguro de atalhos (.lnk) no Menu Iniciar do Windows.
/// Apenas lê nomes e caminhos de arquivo, nunca resolvendo ou executando o atalho.
/// </summary>
public static class StartMenuShortcutScanner
{
    private static readonly HashSet<string> ExcludedNameKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "uninstall",
        "desinstalar",
        "remove",
        "remover",
        "setup",
        "install",
        "instalar",
        "readme",
        "leia-me",
        "leiame",
        "help",
        "ajuda",
        "documentation",
        "documentacao",
        "manual"
    };

    public static List<DiscoveredApp> Scan(IEnumerable<string>? searchDirectories = null)
    {
        var roots = searchDirectories?.ToList() ?? GetDefaultStartMenuRoots();
        var discovered = new List<DiscoveredApp>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var lnkFiles = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnkPath in lnkFiles)
                {
                    if (seenPaths.Contains(lnkPath))
                    {
                        continue;
                    }

                    seenPaths.Add(lnkPath);

                    var name = Path.GetFileNameWithoutExtension(lnkPath);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (ShouldExclude(name))
                    {
                        continue;
                    }

                    var suggestedKey = GenerateSuggestedKey(name);
                    if (string.IsNullOrWhiteSpace(suggestedKey))
                    {
                        continue;
                    }

                    discovered.Add(new DiscoveredApp(name, suggestedKey, lnkPath));
                }
            }
            catch
            {
                // Ignora falhas de permissão em subpastas protegidas
            }
        }

        return discovered
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .DistinctBy(a => a.ShortcutPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetDefaultStartMenuRoots()
    {
        var list = new List<string>();

        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        if (!string.IsNullOrWhiteSpace(commonStartMenu))
        {
            var commonPrograms = Path.Combine(commonStartMenu, "Programs");
            list.Add(Directory.Exists(commonPrograms) ? commonPrograms : commonStartMenu);
        }

        var userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        if (!string.IsNullOrWhiteSpace(userStartMenu))
        {
            var userPrograms = Path.Combine(userStartMenu, "Programs");
            list.Add(Directory.Exists(userPrograms) ? userPrograms : userStartMenu);
        }

        return list;
    }

    private static bool ShouldExclude(string name)
    {
        var lower = name.ToLowerInvariant();
        return ExcludedNameKeywords.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static string GenerateSuggestedKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var unaccented = RemoveAccents(name.ToLowerInvariant());
        var sb = new StringBuilder(unaccented.Length);

        foreach (var c in unaccented)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append(' ');
            }
        }

        return string.Join(" ", sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string RemoveAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
