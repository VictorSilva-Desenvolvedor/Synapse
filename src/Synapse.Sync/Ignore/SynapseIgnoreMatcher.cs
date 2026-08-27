using System.Text.RegularExpressions;

namespace Synapse.Sync.Ignore;

/// <summary>
/// Mecanismo de lista de exclusão configurável com suporte a padrões glob estilo .gitignore (RF-SYNC.7 / US-SYNC.7).
/// Inclui padrões padrão essenciais para o Obsidian/Synapse e limite de tamanho de anexos (padrão 50MB).
/// </summary>
public sealed class SynapseIgnoreMatcher
{
    public const long DefaultMaxFileSizeBytes = 50 * 1024 * 1024; // 50MB

    private readonly long _maxFileSizeBytes;
    private readonly List<Regex> _compiledPatterns = new();
    private readonly object _lock = new();

    private static readonly string[] DefaultPatterns =
    [
        ".synapse/**",
        ".synapse",
        "_conflitos/**",
        "_conflitos",
        ".git/**",
        ".git",
        ".trash/**",
        ".trash",
        ".obsidian/workspace*.json",
        ".obsidian/cache/**",
        "*.tmp",
        "*.crswap",
        "*.~*",
        "Thumbs.db",
        "desktop.ini",
        ".DS_Store"
    ];

    public SynapseIgnoreMatcher(long maxFileSizeBytes = DefaultMaxFileSizeBytes, string? initialIgnoreContent = null)
    {
        _maxFileSizeBytes = maxFileSizeBytes;
        LoadPatterns(initialIgnoreContent);
    }

    public void LoadPatterns(string? ignoreFileContent)
    {
        lock (_lock)
        {
            _compiledPatterns.Clear();

            // 1. Carrega padrões padrão obrigatórios
            foreach (var pattern in DefaultPatterns)
            {
                AddPatternInternal(pattern);
            }

            // 2. Carrega padrões customizados do arquivo .synapseignore
            if (!string.IsNullOrWhiteSpace(ignoreFileContent))
            {
                var lines = ignoreFileContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    {
                        continue; // Comentários ou linhas em branco
                    }

                    AddPatternInternal(trimmed);
                }
            }
        }
    }

    public void LoadFromFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                var content = File.ReadAllText(filePath);
                LoadPatterns(content);
            }
            catch
            {
                LoadPatterns(null);
            }
        }
        else
        {
            LoadPatterns(null);
        }
    }

    public bool ShouldIgnore(string relativePath, long? fileSizeBytes = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return true;
        }

        // Verifica limite de tamanho se informado
        if (fileSizeBytes.HasValue && fileSizeBytes.Value > _maxFileSizeBytes)
        {
            return true;
        }

        var normalizedPath = NormalizePath(relativePath);

        lock (_lock)
        {
            foreach (var regex in _compiledPatterns)
            {
                if (regex.IsMatch(normalizedPath))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void AddPatternInternal(string pattern)
    {
        var regex = GlobToRegex(pattern);
        if (regex != null)
        {
            _compiledPatterns.Add(regex);
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static Regex? GlobToRegex(string globPattern)
    {
        var pattern = NormalizePath(globPattern).TrimEnd('/');
        var isDirectoryOnly = globPattern.EndsWith('/') || globPattern.EndsWith('\\');

        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        var regexPattern = "^";

        if (pattern.StartsWith("**/"))
        {
            regexPattern += "(.+/)?";
            pattern = pattern[3..];
        }
        else if (!pattern.Contains('/'))
        {
            // Padrão sem barra casa com o nome do arquivo em qualquer nível de pasta
            regexPattern += "(.+/)?";
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                // Padrão **
                if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                {
                    regexPattern += "(.+/)?";
                    i += 2;
                }
                else
                {
                    regexPattern += ".*";
                    i++;
                }
            }
            else if (c == '*')
            {
                regexPattern += "[^/]*";
            }
            else if (c == '?')
            {
                regexPattern += "[^/]";
            }
            else if (".+$()^[]{}|\\".Contains(c))
            {
                regexPattern += "\\" + c;
            }
            else
            {
                regexPattern += c;
            }
        }

        if (isDirectoryOnly)
        {
            regexPattern += "(/.*)?$";
        }
        else
        {
            regexPattern += "(/.*)?$";
        }

        try
        {
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        catch
        {
            return null;
        }
    }
}
