using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Synapse.Agent.Models;
using Synapse.Core.Logging;
using Synapse.Core.Ports;

namespace Synapse.Agent;

/// <summary>
/// Poller de comandos remotos via GitHub Relay (ADR-017, Fase 1).
/// Monitora .synapse/remote/commands/, executa os comandos em temp files e publica resultados em .synapse/remote/results/.
/// </summary>
public sealed class RemoteCommandPoller
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };

    // Encoding.UTF8 escreve um BOM no inicio do arquivo por padrao. O PWA remoto decodifica
    // o arquivo (base64 -> texto) e chama JSON.parse direto - um BOM sobrando vira um
    // caractere invisivel antes do '{' que quebra o parse com SyntaxError. Como o parse
    // falha DEPOIS do polling ja ter encontrado o arquivo (parando de tentar de novo), o
    // resultado nunca chegava a aparecer: o app do celular ficava preso em "Pensando..."
    // para sempre, mesmo com o resultado certo ja disponivel no GitHub.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ICloudProvider _cloudProvider;
    private readonly RemoteCommandExecutor _executor;
    private readonly RemoteAuditLog _auditLog;
    private readonly string _cursorFilePath;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RemoteCommandPoller>? _logger;

    public RemoteCommandPoller(
        ICloudProvider cloudProvider,
        RemoteCommandExecutor executor,
        RemoteAuditLog auditLog,
        string? cursorFilePath = null,
        TimeSpan? interval = null,
        TimeProvider? timeProvider = null,
        ILogger<RemoteCommandPoller>? logger = null)
    {
        _cloudProvider = cloudProvider ?? throw new ArgumentNullException(nameof(cloudProvider));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _cursorFilePath = cursorFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Synapse",
            "remote_agent_cursor.txt");
        _interval = interval ?? DefaultInterval;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Executa um ciclo único de verificação de comandos remotos.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        try
        {
            var currentToken = await LoadCursorAsync(ct) ?? await _cloudProvider.GetStartPageTokenAsync(ct);
            string? newStartToken = null;

            while (currentToken is not null && !ct.IsCancellationRequested)
            {
                var page = await _cloudProvider.GetChangesAsync(currentToken, ct);

                foreach (var changedFile in page.ChangedFiles)
                {
                    if (changedFile.Trashed) continue;

                    var normalizedPath = changedFile.Id.Replace('\\', '/').TrimStart('/');
                    if (normalizedPath.StartsWith(".synapse/remote/commands/", StringComparison.OrdinalIgnoreCase) &&
                        normalizedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProcessCommandFileAsync(normalizedPath, ct);
                    }
                }

                newStartToken ??= page.NewStartPageToken;
                currentToken = page.NextPageToken;
            }

            if (newStartToken is not null)
            {
                await SaveCursorAsync(newStartToken, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Encerramento limpo
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro no ciclo de verificação do RemoteCommandPoller.");
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "RemoteAgent",
                "CheckCycleError",
                status: "Failed",
                errorMessage: ex.Message);
        }
    }

    private async Task ProcessCommandFileAsync(string cloudPath, CancellationToken ct)
    {
        var tempDownloadPath = Path.Combine(Path.GetTempPath(), $"synapse-cmd-{Guid.NewGuid():N}.json");
        var tempResultPath = Path.Combine(Path.GetTempPath(), $"synapse-res-{Guid.NewGuid():N}.json");

        try
        {
            _logger?.LogInformation("Baixando comando remoto de '{CloudPath}'...", cloudPath);
            await _cloudProvider.DownloadAsync(cloudPath, tempDownloadPath, ct);

            if (!File.Exists(tempDownloadPath))
            {
                _logger?.LogWarning("Arquivo temporário de comando não foi gravado: {Path}", tempDownloadPath);
                return;
            }

            var json = await File.ReadAllTextAsync(tempDownloadPath, ct);
            var command = JsonSerializer.Deserialize<RemoteCommand>(json, JsonOptions);

            if (command == null)
            {
                _logger?.LogWarning("Falha ao desserializar comando remoto de '{CloudPath}'", cloudPath);
                return;
            }

            _logger?.LogInformation("Executando comando remoto {CommandId} ({Type}) solicitado por '{RequestedBy}'...", command.Id, command.Type, command.RequestedBy);

            // 1. Executa o comando via executor seguro
            var result = await _executor.ExecuteAsync(command, ct);

            // 2. Grava trilha de auditoria
            await _auditLog.LogEntryAsync(command, result, ct);

            // 3. Serializa o resultado e sobe para o GitHub
            var resultJson = JsonSerializer.Serialize(result, JsonOptions);
            var resultFileName = $"{command.Id}.json";
            var resultDir = Path.GetDirectoryName(tempResultPath);
            if (!string.IsNullOrEmpty(resultDir)) Directory.CreateDirectory(resultDir);

            // Grava o arquivo com o nome exato esperado
            var namedResultPath = Path.Combine(resultDir ?? Path.GetTempPath(), resultFileName);
            await File.WriteAllTextAsync(namedResultPath, resultJson, Utf8NoBom, ct);

            _logger?.LogInformation("Enviando resultado do comando {CommandId} ({Status}) para o GitHub...", command.Id, result.Status);
            await _cloudProvider.UploadAsync(namedResultPath, ".synapse/remote/results", ct);

            if (File.Exists(namedResultPath)) File.Delete(namedResultPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao processar arquivo de comando remoto '{CloudPath}'", cloudPath);
            _ = SynapseActivityLogger.Instance.LogActionAsync(
                "RemoteAgent",
                "ProcessCommandFileError",
                details: cloudPath,
                status: "Failed",
                errorMessage: ex.Message);
        }
        finally
        {
            if (File.Exists(tempDownloadPath))
            {
                try { File.Delete(tempDownloadPath); } catch { }
            }
            if (File.Exists(tempResultPath))
            {
                try { File.Delete(tempResultPath); } catch { }
            }
        }
    }

    private async Task<string?> LoadCursorAsync(CancellationToken ct)
    {
        if (!File.Exists(_cursorFilePath)) return null;

        try
        {
            var text = (await File.ReadAllTextAsync(_cursorFilePath, ct)).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveCursorAsync(string cursor, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cursorFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(_cursorFilePath, cursor.Trim(), Utf8NoBom, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Falha ao persistir cursor do agente remoto em '{Path}'", _cursorFilePath);
        }
    }

    /// <summary>
    /// Loop de longa duração do poller. RunOnceAsync já engole exceções do próprio ciclo
    /// de verificação, mas esse laço externo (incluindo a espera do PeriodicTimer) também
    /// precisa ser resiliente: uma exceção não tratada aqui mataria a tarefa de polling
    /// silenciosamente para sempre (ela roda "fire-and-forget"), e o Controle Remoto
    /// pareceria "ativado" no menu sem nunca mais processar nada, sem nenhum erro visível.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Erro inesperado no loop do RemoteCommandPoller. Continuando no próximo ciclo.");
                _ = SynapseActivityLogger.Instance.LogActionAsync(
                    "RemoteAgent",
                    "PollerLoopError",
                    status: "Failed",
                    errorMessage: ex.Message);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
