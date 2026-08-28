using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Synapse.Core.Logging;

/// <summary>
/// Motor central de log de atividades e auditoria do Synapse.
/// Grava simultaneamente:
/// 1. Localmente em %LOCALAPPDATA%\Synapse\Logs (JSONL e LOG)
/// 2. No Obsidian Vault em .synapse/logs e em notas Markdown diárias (Synapse/Logs/Registro de Atividades — AAAA-MM-DD.md)
/// </summary>
public sealed class SynapseActivityLogger
{
    private static readonly Lazy<SynapseActivityLogger> _instance = new(() => new SynapseActivityLogger());
    public static SynapseActivityLogger Instance => _instance.Value;

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _localLogsDir;
    private string? _vaultPath;
    private readonly string _appVersion;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public const int MaxOperationTimeoutMs = 120_000; // 2 minutos máximo

    public SynapseActivityLogger(string? customLocalLogsDir = null, string? vaultPath = null)
    {
        _localLogsDir = customLocalLogsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Synapse",
            "Logs");

        _vaultPath = vaultPath;

        _appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "1.0.0";

        try
        {
            Directory.CreateDirectory(_localLogsDir);
        }
        catch
        {
            // Ignora falhas iniciais na criação de diretório
        }
    }

    /// <summary>
    /// Configura dinamicamente o caminho do cofre para sincronização dos logs dentro do Obsidian.
    /// </summary>
    public void SetVaultPath(string? vaultPath)
    {
        if (!string.IsNullOrWhiteSpace(vaultPath) && Directory.Exists(vaultPath))
        {
            _vaultPath = vaultPath;
        }
    }

    /// <summary>
    /// Registra um clique de interface ou ação de usuário no sistema.
    /// </summary>
    public Task LogClickAsync(
        string component,
        string elementName,
        string? details = null,
        CancellationToken ct = default)
    {
        return LogAsync(new SynapseActivityEntry
        {
            Component = component,
            Action = "UserClick",
            Details = $"Element: {elementName}" + (details != null ? $" | {details}" : ""),
            Status = "Success"
        }, ct);
    }

    /// <summary>
    /// Registra uma ação executada no sistema com status e duração opcional.
    /// </summary>
    public Task LogActionAsync(
        string component,
        string action,
        string? details = null,
        string status = "Success",
        long? durationMs = null,
        string? errorMessage = null,
        string? affectedPath = null,
        CancellationToken ct = default)
    {
        return LogAsync(new SynapseActivityEntry
        {
            Component = component,
            Action = action,
            Details = details,
            Status = status,
            DurationMs = durationMs,
            ErrorMessage = errorMessage,
            AffectedPath = affectedPath
        }, ct);
    }

    /// <summary>
    /// Registra uma interação completa de pergunta e resposta (Chat / RAG / Brain).
    /// </summary>
    public Task LogChatAsync(
        string question,
        string answer,
        long durationMs,
        string status = "Success",
        IReadOnlyList<string>? notesConsulted = null,
        string? savedNotePath = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var detailsBuilder = new StringBuilder();
        if (notesConsulted != null && notesConsulted.Count > 0)
        {
            detailsBuilder.Append($"Notas Consultadas: [{string.Join(", ", notesConsulted)}]");
        }
        if (!string.IsNullOrWhiteSpace(savedNotePath))
        {
            if (detailsBuilder.Length > 0) detailsBuilder.Append(" | ");
            detailsBuilder.Append($"Nota Salva: {savedNotePath}");
        }

        return LogAsync(new SynapseActivityEntry
        {
            Component = "ChatVault",
            Action = "ChatTurn",
            Question = question,
            Answer = answer,
            DurationMs = durationMs,
            Status = durationMs >= MaxOperationTimeoutMs ? "Timeout" : status,
            Details = detailsBuilder.Length > 0 ? detailsBuilder.ToString() : null,
            ErrorMessage = errorMessage,
            AffectedPath = savedNotePath
        }, ct);
    }

    /// <summary>
    /// Executa e rastreia o tempo de resposta de uma operação assíncrona, aplicando timeout máximo de 2 minutos
    /// e registrando o log completo automaticamente.
    /// </summary>
    public async Task<T> TrackOperationAsync<T>(
        string component,
        string action,
        string? details,
        Func<CancellationToken, Task<T>> operation,
        int timeoutMs = MaxOperationTimeoutMs,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            var result = await operation(cts.Token);
            sw.Stop();

            await LogAsync(new SynapseActivityEntry
            {
                Component = component,
                Action = action,
                Details = details,
                DurationMs = sw.ElapsedMilliseconds,
                Status = "Success"
            }, ct);

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            await LogAsync(new SynapseActivityEntry
            {
                Component = component,
                Action = action,
                Details = details,
                DurationMs = sw.ElapsedMilliseconds,
                Status = "Timeout",
                ErrorMessage = $"A operação excedeu o tempo limite máximo de {timeoutMs / 1000}s (2 minutos)."
            }, CancellationToken.None);

            throw new TimeoutException($"Operação '{action}' em '{component}' expirou após {timeoutMs / 1000}s.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            await LogAsync(new SynapseActivityEntry
            {
                Component = component,
                Action = action,
                Details = details,
                DurationMs = sw.ElapsedMilliseconds,
                Status = "Failed",
                ErrorMessage = ex.Message
            }, CancellationToken.None);

            throw;
        }
    }

    /// <summary>
    /// Grava a entrada de log nos destinos locais e no cofre do Obsidian.
    /// </summary>
    public async Task LogAsync(SynapseActivityEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        entry.AppVersion = _appVersion;
        var jsonLine = JsonSerializer.Serialize(entry, _jsonOptions);

        await _fileLock.WaitAsync(ct);
        try
        {
            // 1. Destino Local: %LOCALAPPDATA%\Synapse\Logs
            await AppendLocalLogsAsync(entry, jsonLine, ct);

            // 2. Destino Obsidian Vault (se configurado)
            if (!string.IsNullOrWhiteSpace(_vaultPath) && Directory.Exists(_vaultPath))
            {
                await AppendVaultLogsAsync(entry, jsonLine, ct);
            }
        }
        catch
        {
            // O sistema de logs nunca deve quebrar a aplicação caso ocorra falha de I/O em disco
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task AppendLocalLogsAsync(SynapseActivityEntry entry, string jsonLine, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_localLogsDir);

            // 1.1 Arquivo JSONL
            var jsonlPath = Path.Combine(_localLogsDir, "synapse_activity.jsonl");
            await File.AppendAllTextAsync(jsonlPath, jsonLine + Environment.NewLine, Encoding.UTF8, ct);

            // 1.2 Arquivo .log legível
            var logPath = Path.Combine(_localLogsDir, "synapse_activity.log");
            var readableLine = FormatReadableLogLine(entry);
            await File.AppendAllTextAsync(logPath, readableLine + Environment.NewLine, Encoding.UTF8, ct);
        }
        catch
        {
            // Ignora falha local transitória
        }
    }

    private async Task AppendVaultLogsAsync(SynapseActivityEntry entry, string jsonLine, CancellationToken ct)
    {
        try
        {
            // 2.1 JSONL interno no cofre: .synapse/logs/synapse-activity.jsonl
            var hiddenLogDir = Path.Combine(_vaultPath!, ".synapse", "logs");
            Directory.CreateDirectory(hiddenLogDir);
            var hiddenLogFile = Path.Combine(hiddenLogDir, "synapse-activity.jsonl");
            await File.AppendAllTextAsync(hiddenLogFile, jsonLine + Environment.NewLine, Encoding.UTF8, ct);

            // 2.2 Nota Markdown visível e navegável no Obsidian: Synapse/Logs/Registro de Atividades — AAAA-MM-DD.md
            var visibleLogDir = Path.Combine(_vaultPath!, "Synapse", "Logs");
            Directory.CreateDirectory(visibleLogDir);

            var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            var markdownNotePath = Path.Combine(visibleLogDir, $"Registro de Atividades — {todayStr}.md");

            if (!File.Exists(markdownNotePath))
            {
                var initialHeader = $@"---
titulo: ""Registro de Atividades — {todayStr}""
categoria: ""Logs""
data: ""{todayStr}""
versao_app: ""{_appVersion}""
tags:
  - synapse-logs
  - auditoria
---

# 📊 Registro de Atividades — {todayStr}

Registro contínuo e em tempo real de cliques, comandos, perguntas, respostas da IA e tempos de resposta do Synapse.

| Horário | Componente | Ação | Duração | Status | Detalhes |
| :--- | :--- | :--- | :--- | :--- | :--- |
";
                await File.WriteAllTextAsync(markdownNotePath, initialHeader, Encoding.UTF8, ct);
            }

            var durationText = entry.DurationMs.HasValue ? $"{entry.DurationMs.Value}ms" : "-";
            var statusIcon = entry.Status switch
            {
                "Success" => "✅ Sucesso",
                "Timeout" => "⏱️ Timeout (>2min)",
                "Failed" => "❌ Falha",
                "Warning" => "⚠️ Alerta",
                _ => entry.Status
            };

            var safeDetails = (entry.Details ?? entry.ErrorMessage ?? "-").Replace("|", "\\|").Replace("\n", " ").Trim();
            if (safeDetails.Length > 120) safeDetails = safeDetails[..117] + "...";

            var tableRow = $"| {entry.LocalTime[11..19]} | `{entry.Component}` | **{entry.Action}** | `{durationText}` | {statusIcon} | {safeDetails} |{Environment.NewLine}";

            // Se for interação com Pergunta e Resposta, adiciona bloco de detalhe formatado
            if (!string.IsNullOrWhiteSpace(entry.Question))
            {
                var qBlock = $@"{Environment.NewLine}### 💬 Interação às {entry.LocalTime[11..19]} (`{durationText}`)
- **Pergunta:** {entry.Question}
- **Resposta:**
{entry.Answer ?? "*(Sem resposta)*"}
{(entry.ErrorMessage != null ? $"- **Erro:** {entry.ErrorMessage}{Environment.NewLine}" : "")}---
";
                await File.AppendAllTextAsync(markdownNotePath, tableRow + qBlock, Encoding.UTF8, ct);
            }
            else
            {
                await File.AppendAllTextAsync(markdownNotePath, tableRow, Encoding.UTF8, ct);
            }
        }
        catch
        {
            // Ignora falha de escrita no cofre se o Obsidian ou arquivo estiver bloqueado
        }
    }

    private static string FormatReadableLogLine(SynapseActivityEntry entry)
    {
        var dur = entry.DurationMs.HasValue ? $" [{entry.DurationMs.Value}ms]" : "";
        var err = !string.IsNullOrWhiteSpace(entry.ErrorMessage) ? $" | ERROR: {entry.ErrorMessage}" : "";
        var q = !string.IsNullOrWhiteSpace(entry.Question) ? $" | Q: \"{entry.Question}\"" : "";
        var a = !string.IsNullOrWhiteSpace(entry.Answer) ? $" | A: \"{entry.Answer.Replace("\r", "").Replace("\n", " ")}\"" : "";
        return $"[{entry.LocalTime}] [v{entry.AppVersion}] [{entry.Component}] [{entry.Action}] ({entry.Status}){dur} - {entry.Details}{q}{a}{err}";
    }
}
