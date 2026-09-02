using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Synapse.Search;

public sealed class RipgrepSearchEngine : IRawSearchEngine
{
    private readonly string _rgPath;

    public RipgrepSearchEngine(string? customRgPath = null)
    {
        _rgPath = customRgPath ?? ResolveRipgrepPath();
    }

    public async IAsyncEnumerable<RipgrepMatch> SearchAsync(
        string vaultRootPath,
        string pattern,
        bool isRegex = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultRootPath))
        {
            throw new ArgumentException("Vault root path cannot be null or whitespace.", nameof(vaultRootPath));
        }

        if (!Directory.Exists(vaultRootPath))
        {
            throw new DirectoryNotFoundException($"Vault root directory does not exist: '{vaultRootPath}'");
        }

        ArgumentNullException.ThrowIfNull(pattern);

        var psi = new ProcessStartInfo
        {
            FileName = _rgPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add("--json");

        if (!isRegex)
        {
            psi.ArgumentList.Add("--fixed-strings");
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(pattern);
        psi.ArgumentList.Add(vaultRootPath);

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start ripgrep process at '{_rgPath}'.");
        }

        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignored on cancellation cleanup
            }
        });

        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            while (!process.StandardOutput.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();

                string? line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                RipgrepMatch? match = null;
                try
                {
                    match = ParseRipgrepJsonLine(line);
                }
                catch (JsonException)
                {
                    // Ignore non-json or malformed output lines
                }

                if (match != null)
                {
                    yield return match;
                }
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (process.ExitCode != 0 && process.ExitCode != 1)
            {
                throw new InvalidOperationException(
                    $"Ripgrep process exited with code {process.ExitCode}: {stderr.Trim()}");
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignored on disposal cleanup
            }
        }
    }

    private static RipgrepMatch? ParseRipgrepJsonLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "match")
        {
            return null;
        }

        if (!root.TryGetProperty("data", out var dataProp))
        {
            return null;
        }

        string filePath = string.Empty;
        if (dataProp.TryGetProperty("path", out var pathProp) &&
            pathProp.TryGetProperty("text", out var pathTextProp))
        {
            filePath = pathTextProp.GetString() ?? string.Empty;
        }

        int lineNumber = 0;
        if (dataProp.TryGetProperty("line_number", out var lineNumProp))
        {
            lineNumber = lineNumProp.GetInt32();
        }

        string lineText = string.Empty;
        if (dataProp.TryGetProperty("lines", out var linesProp) &&
            linesProp.TryGetProperty("text", out var linesTextProp))
        {
            lineText = linesTextProp.GetString()?.TrimEnd('\r', '\n') ?? string.Empty;
        }

        int matchStart = 0;
        int matchEnd = 0;
        if (dataProp.TryGetProperty("submatches", out var submatchesProp) &&
            submatchesProp.ValueKind == JsonValueKind.Array &&
            submatchesProp.GetArrayLength() > 0)
        {
            var firstSubmatch = submatchesProp[0];
            if (firstSubmatch.TryGetProperty("start", out var startProp))
            {
                matchStart = startProp.GetInt32();
            }
            if (firstSubmatch.TryGetProperty("end", out var endProp))
            {
                matchEnd = endProp.GetInt32();
            }
        }

        return new RipgrepMatch(filePath, lineNumber, lineText, matchStart, matchEnd);
    }

    private static string ResolveRipgrepPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidateInTools = Path.Combine(baseDir, "Tools", "rg.exe");
        if (File.Exists(candidateInTools))
        {
            return candidateInTools;
        }

        var candidateInRoot = Path.Combine(baseDir, "rg.exe");
        if (File.Exists(candidateInRoot))
        {
            return candidateInRoot;
        }

        throw new FileNotFoundException(
            $"Ripgrep executable (rg.exe) was not found in '{candidateInTools}' or '{candidateInRoot}'.");
    }
}
