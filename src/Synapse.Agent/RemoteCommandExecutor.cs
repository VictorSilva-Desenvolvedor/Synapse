using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Synapse.Agent.Models;
using Synapse.Brain.Ports;
using Synapse.Core.Logging;
using Synapse.Sync.Config;

namespace Synapse.Agent;

/// <summary>
/// Executor seguro de comandos remotos com validação de allowlist, proteção contra path traversal, confirmação interativa para ações sensíveis e RAG contra o cofre (Fases 1, 2 e 4).
/// </summary>
public sealed class RemoteCommandExecutor
{
    private readonly Func<Task<SynapseConfig>> _configProvider;
    private readonly IRemoteConfirmationPrompt? _confirmationPrompt;
    private readonly IUiAutomationAdapter? _uiAutomation;
    private readonly IVaultBrainQuery? _brainQuery;
    private readonly RemoteAuditLog? _auditLog;
    private readonly ILogger<RemoteCommandExecutor>? _logger;

    public RemoteCommandExecutor(
        SynapseConfig config,
        IRemoteConfirmationPrompt? confirmationPrompt = null,
        IUiAutomationAdapter? uiAutomation = null,
        IVaultBrainQuery? brainQuery = null,
        RemoteAuditLog? auditLog = null,
        ILogger<RemoteCommandExecutor>? logger = null)
        : this(() => Task.FromResult(config), confirmationPrompt, uiAutomation, brainQuery, auditLog, logger)
    {
    }

    /// <summary>
    /// configProvider e assincrono de proposito: um Func&lt;SynapseConfig&gt; sincrono forcaria
    /// o chamador a bloquear com GetAwaiter().GetResult() para ler config de disco, o que
    /// trava para sempre quando esse bloqueio acontece na thread de UI do WPF (nenhum
    /// ConfigureAwait(false) e usado neste codebase, entao a continuacao do LoadAsync()
    /// tentaria voltar pra mesma thread ja bloqueada). Foi exatamente essa combinacao que
    /// derrubou o RemoteCommandPoller silenciosamente ao processar o primeiro comando real.
    /// </summary>
    public RemoteCommandExecutor(
        Func<Task<SynapseConfig>> configProvider,
        IRemoteConfirmationPrompt? confirmationPrompt = null,
        IUiAutomationAdapter? uiAutomation = null,
        IVaultBrainQuery? brainQuery = null,
        RemoteAuditLog? auditLog = null,
        ILogger<RemoteCommandExecutor>? logger = null)
    {
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _confirmationPrompt = confirmationPrompt;
        _uiAutomation = uiAutomation;
        _brainQuery = brainQuery;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<RemoteCommandResult> ExecuteAsync(RemoteCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var config = await _configProvider();

        // 1. Verificação global do interruptor de segurança
        if (!config.RemoteControlEnabled)
        {
            _logger?.LogWarning("Comando remoto {CommandId} ({Type}) rejeitado: Controle remoto desativado nas configurações.", command.Id, command.Type);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Controle remoto desativado");
        }

        try
        {
            return command.Type switch
            {
                RemoteCommandType.OpenApp => ExecuteOpenApp(command, config),
                RemoteCommandType.OpenNote => ExecuteOpenNote(command, config),
                RemoteCommandType.FocusWindow => ExecuteFocusWindow(command),
                RemoteCommandType.TypeText => await ExecuteTypeTextAsync(command, config, ct),
                RemoteCommandType.ClickElement => await ExecuteClickElementAsync(command, config, ct),
                RemoteCommandType.AskVault => await ExecuteAskVaultAsync(command, config, ct),
                _ => new RemoteCommandResult(
                    command.Id,
                    DateTimeOffset.UtcNow,
                    RemoteCommandStatus.Rejected,
                    $"Tipo de comando '{command.Type}' desconhecido ou não suportado.")
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro inesperado ao executar comando remoto {CommandId} ({Type})", command.Id, command.Type);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Erro interno na execução: {ex.Message}");
        }
    }

    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abre", "abra", "abrir", "abrindo",
        "open", "launch",
        "inicia", "iniciar",
        "executa", "executar", "execute",
        "por", "favor",
        "o", "a", "os", "as",
        "aplicativo", "aplicativos", "app", "apps",
        "programa", "programas"
    };

    private RemoteCommandResult ExecuteOpenApp(RemoteCommand command, SynapseConfig config)
    {
        if (command.Payload == null || !command.Payload.TryGetValue("app", out var rawAppKey) || string.IsNullOrWhiteSpace(rawAppKey))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'app' não informado no payload.");
        }

        var matchedKey = ResolveAllowedAppKey(rawAppKey, config.RemoteAllowedApps);

        if (matchedKey is null || !config.RemoteAllowedApps.TryGetValue(matchedKey, out var appPath) || string.IsNullOrWhiteSpace(appPath))
        {
            _logger?.LogWarning("Tentativa de abrir aplicativo não permitido: '{AppKey}'", rawAppKey);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                $"Aplicativo '{rawAppKey}' não está na lista de aplicativos permitidos (RemoteAllowedApps).");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true
            };

            Process.Start(psi);
            _logger?.LogInformation("Aplicativo '{AppKey}' ({Path}) iniciado com sucesso via comando remoto {CommandId}.", matchedKey, appPath, command.Id);

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Aplicativo '{matchedKey}' iniciado com sucesso.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Falha ao iniciar processo '{Path}' para o app '{AppKey}'", appPath, matchedKey);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Falha ao iniciar aplicativo '{matchedKey}': {ex.Message}");
        }
    }

    internal static string? ResolveAllowedAppKey(string rawInput, IReadOnlyDictionary<string, string> allowedApps)
    {
        if (allowedApps == null || allowedApps.Count == 0 || string.IsNullOrWhiteSpace(rawInput))
        {
            return null;
        }

        var trimmedInput = rawInput.Trim();

        // 1. Match exato de chave (case-insensitive)
        foreach (var key in allowedApps.Keys)
        {
            if (string.Equals(key.Trim(), trimmedInput, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        // 2. Normalizar rawInput: minúsculas, sem acentos, sem pontuação, colapsar espaços
        var normalizedInput = NormalizeText(trimmedInput);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return null;
        }

        // 3. Remover palavras de preenchimento comuns (pt/en) quando aparecerem como palavra inteira
        var inputWords = normalizedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filteredWords = inputWords.Where(w => !FillerWords.Contains(w)).ToArray();
        var cleanedInput = filteredWords.Length > 0
            ? string.Join(" ", filteredWords)
            : normalizedInput;

        if (string.IsNullOrWhiteSpace(cleanedInput))
        {
            cleanedInput = normalizedInput;
        }

        // 4. Mapear cada entrada da allowlist
        var candidates = new List<(string Key, string NormalizedKey, string? NormalizedDisplayName)>();
        foreach (var (key, value) in allowedApps)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;

            var normKey = NormalizeText(key);
            string? normDisplayName = null;

            if (!string.IsNullOrWhiteSpace(value) && !value.Contains("://", StringComparison.Ordinal))
            {
                try
                {
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(value.Trim());
                    if (!string.IsNullOrWhiteSpace(fileNameWithoutExt))
                    {
                        normDisplayName = NormalizeText(fileNameWithoutExt);
                    }
                }
                catch
                {
                    // Ignora nome de exibição caso o caminho contenha caracteres inválidos
                }
            }

            candidates.Add((key, normKey, normDisplayName));
        }

        // 5. Ordem de precedência:
        // Nível a: entrada limpa == chave normalizada
        var matchesA = candidates
            .Where(c => !string.IsNullOrEmpty(c.NormalizedKey) && string.Equals(cleanedInput, c.NormalizedKey, StringComparison.Ordinal))
            .Select(c => c.Key)
            .Distinct()
            .ToList();

        if (matchesA.Count == 1) return matchesA[0];
        if (matchesA.Count > 1) return null; // Ambiguidade

        // Nível b: entrada limpa == nome de exibição normalizado
        var matchesB = candidates
            .Where(c => !string.IsNullOrEmpty(c.NormalizedDisplayName) && string.Equals(cleanedInput, c.NormalizedDisplayName, StringComparison.Ordinal))
            .Select(c => c.Key)
            .Distinct()
            .ToList();

        if (matchesB.Count == 1) return matchesB[0];
        if (matchesB.Count > 1) return null;

        // Nível c: substring nos dois sentidos (mín. 3 caracteres no lado mais curto) contra a chave normalizada
        var matchesC = candidates
            .Where(c => !string.IsNullOrEmpty(c.NormalizedKey)
                && Math.Min(cleanedInput.Length, c.NormalizedKey.Length) >= 3
                && (c.NormalizedKey.Contains(cleanedInput, StringComparison.Ordinal) || cleanedInput.Contains(c.NormalizedKey, StringComparison.Ordinal)))
            .Select(c => c.Key)
            .Distinct()
            .ToList();

        if (matchesC.Count == 1) return matchesC[0];
        if (matchesC.Count > 1) return null;

        // Nível d: mesmo teste de substring contra o nome de exibição normalizado
        var matchesD = candidates
            .Where(c => !string.IsNullOrEmpty(c.NormalizedDisplayName)
                && Math.Min(cleanedInput.Length, c.NormalizedDisplayName!.Length) >= 3
                && (c.NormalizedDisplayName.Contains(cleanedInput, StringComparison.Ordinal) || cleanedInput.Contains(c.NormalizedDisplayName, StringComparison.Ordinal)))
            .Select(c => c.Key)
            .Distinct()
            .ToList();

        if (matchesD.Count == 1) return matchesD[0];
        if (matchesD.Count > 1) return null;

        // Nível e: distância de Levenshtein contra a chave normalizada
        var matchesE = candidates
            .Where(c =>
            {
                if (string.IsNullOrEmpty(c.NormalizedKey)) return false;
                var maxDist = c.NormalizedKey.Length < 5 ? 1 : 2;
                var dist = LevenshteinDistance(cleanedInput, c.NormalizedKey);
                return dist <= maxDist;
            })
            .Select(c => c.Key)
            .Distinct()
            .ToList();

        if (matchesE.Count == 1) return matchesE[0];
        if (matchesE.Count > 1) return null;

        return null;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var unaccented = RemoveAccents(text.ToLowerInvariant());
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

    private static int LevenshteinDistance(string s, string t)
    {
        if (string.Equals(s, t, StringComparison.Ordinal)) return 0;
        if (s.Length == 0) return t.Length;
        if (t.Length == 0) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;

        for (var i = 1; i <= s.Length; i++)
        {
            for (var j = 1; j <= t.Length; j++)
            {
                var cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
    }

    private RemoteCommandResult ExecuteOpenNote(RemoteCommand command, SynapseConfig config)
    {
        if (command.Payload == null || !command.Payload.TryGetValue("relativePath", out var relativePath) || string.IsNullOrWhiteSpace(relativePath))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'relativePath' não informado no payload.");
        }

        if (string.IsNullOrWhiteSpace(config.VaultPath))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Cofre local não configurado.");
        }

        var vaultRootFull = Path.GetFullPath(config.VaultPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFullPath = Path.GetFullPath(Path.Combine(vaultRootFull, relativePath));

        // Proteção estrita contra Path Traversal
        var vaultPrefix = vaultRootFull + Path.DirectorySeparatorChar;
        if (!targetFullPath.StartsWith(vaultPrefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(targetFullPath, vaultRootFull, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning("Bloqueio de Path Traversal no comando {CommandId}: '{Target}' fora de '{Vault}'", command.Id, targetFullPath, vaultRootFull);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Acesso negado: o caminho da nota ultrapassa o diretório raiz do cofre (Path Traversal bloqueado).");
        }

        if (!File.Exists(targetFullPath))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"A nota '{relativePath}' não foi encontrada no cofre.");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetFullPath,
                UseShellExecute = true
            };

            Process.Start(psi);
            _logger?.LogInformation("Nota '{Path}' aberta com sucesso via comando remoto {CommandId}.", relativePath, command.Id);

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Nota '{relativePath}' aberta com sucesso.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Falha ao abrir nota '{Path}'", targetFullPath);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Falha ao abrir nota '{relativePath}': {ex.Message}");
        }
    }

    private RemoteCommandResult ExecuteFocusWindow(RemoteCommand command)
    {
        if (command.Payload == null || !command.Payload.TryGetValue("processName", out var processName) || string.IsNullOrWhiteSpace(processName))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'processName' não informado no payload.");
        }

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        try
        {
            var processes = Process.GetProcessesByName(cleanName);
            if (processes.Length == 0)
            {
                return new RemoteCommandResult(
                    command.Id,
                    DateTimeOffset.UtcNow,
                    RemoteCommandStatus.Success,
                    $"Processo '{cleanName}' não está em execução no momento.");
            }

            var focused = false;
            foreach (var proc in processes)
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(proc.MainWindowHandle);
                    }
                    focused = true;
                    break;
                }
            }

            var msg = focused
                ? $"Janela do processo '{cleanName}' trazida para primeiro plano."
                : $"Processo '{cleanName}' encontrado, mas sem janela visível para focar.";

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                msg);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Falha ao focar janela do processo '{ProcessName}'", cleanName);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Não foi possível focar a janela do processo '{cleanName}': {ex.Message}");
        }
    }

    private async Task<RemoteCommandResult> ExecuteTypeTextAsync(
        RemoteCommand command,
        SynapseConfig config,
        CancellationToken ct)
    {
        if (command.Payload == null ||
            !command.Payload.TryGetValue("processName", out var processName) ||
            string.IsNullOrWhiteSpace(processName) ||
            !command.Payload.TryGetValue("text", out var text) ||
            text is null)
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetros 'processName' e 'text' são obrigatórios para o comando TypeText.");
        }

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        // 1. Validação de Allowlist (reaproveitada de RemoteAllowedApps)
        if (!IsProcessAllowed(processName, config))
        {
            _logger?.LogWarning("Tentativa de digitação em processo não permitido: '{ProcessName}'", processName);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                $"Processo '{processName}' não está na lista de aplicativos permitidos (RemoteAllowedApps).");
        }

        // 2. Confirmação Humana Obrigatória
        if (_confirmationPrompt == null)
        {
            _logger?.LogWarning("Comando sensível TypeText rejeitado: IRemoteConfirmationPrompt não configurado.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada: nenhum mecanismo de confirmação interativa configurado.");
        }

        var timeout = TimeSpan.FromSeconds(
            config.RemoteConfirmationTimeoutSeconds > 0
                ? config.RemoteConfirmationTimeoutSeconds
                : 30);

        var confirmed = await _confirmationPrompt.ConfirmAsync(command, timeout, ct);
        if (!confirmed)
        {
            _logger?.LogInformation("Comando sensível TypeText ({CommandId}) negado ou expirado pelo usuário.", command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada ou não confirmada pelo usuário.");
        }

        // 3. Execução via UI Automation Adapter
        if (_uiAutomation == null)
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                "Adaptador de UI Automation não configurado.");
        }

        var success = _uiAutomation.TrySendText(cleanName, text);
        if (success)
        {
            _logger?.LogInformation("Texto digitado com sucesso no processo '{ProcessName}' via comando {CommandId}.", cleanName, command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Texto digitado com sucesso no processo '{cleanName}'.");
        }

        return new RemoteCommandResult(
            command.Id,
            DateTimeOffset.UtcNow,
            RemoteCommandStatus.Failed,
            $"Falha ao digitar texto no processo '{cleanName}' (janela não encontrada ou erro no envio).");
    }

    private async Task<RemoteCommandResult> ExecuteClickElementAsync(
        RemoteCommand command,
        SynapseConfig config,
        CancellationToken ct)
    {
        if (command.Payload == null ||
            !command.Payload.TryGetValue("processName", out var processName) ||
            string.IsNullOrWhiteSpace(processName) ||
            !command.Payload.TryGetValue("elementName", out var elementName) ||
            string.IsNullOrWhiteSpace(elementName))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetros 'processName' e 'elementName' são obrigatórios para o comando ClickElement.");
        }

        var cleanName = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        // 1. Validação de Allowlist (reaproveitada de RemoteAllowedApps)
        if (!IsProcessAllowed(processName, config))
        {
            _logger?.LogWarning("Tentativa de clique em processo não permitido: '{ProcessName}'", processName);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                $"Processo '{processName}' não está na lista de aplicativos permitidos (RemoteAllowedApps).");
        }

        // 2. Confirmação Humana Obrigatória
        if (_confirmationPrompt == null)
        {
            _logger?.LogWarning("Comando sensível ClickElement rejeitado: IRemoteConfirmationPrompt não configurado.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada: nenhum mecanismo de confirmação interativa configurado.");
        }

        var timeout = TimeSpan.FromSeconds(
            config.RemoteConfirmationTimeoutSeconds > 0
                ? config.RemoteConfirmationTimeoutSeconds
                : 30);

        var confirmed = await _confirmationPrompt.ConfirmAsync(command, timeout, ct);
        if (!confirmed)
        {
            _logger?.LogInformation("Comando sensível ClickElement ({CommandId}) negado ou expirado pelo usuário.", command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Ação sensível rejeitada ou não confirmada pelo usuário.");
        }

        // 3. Execução via UI Automation Adapter
        if (_uiAutomation == null)
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                "Adaptador de UI Automation não configurado.");
        }

        var success = _uiAutomation.TryClickElement(cleanName, elementName);
        if (success)
        {
            _logger?.LogInformation("Elemento '{ElementName}' clicado com sucesso no processo '{ProcessName}' via comando {CommandId}.", elementName, cleanName, command.Id);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                $"Elemento '{elementName}' clicado com sucesso no processo '{cleanName}'.");
        }

        return new RemoteCommandResult(
            command.Id,
            DateTimeOffset.UtcNow,
            RemoteCommandStatus.Failed,
            $"Elemento '{elementName}' não foi encontrado ou não pôde ser clicado no processo '{cleanName}'.");
    }

    private async Task<RemoteCommandResult> ExecuteAskVaultAsync(
        RemoteCommand command,
        SynapseConfig config,
        CancellationToken ct)
    {
        if (command.Payload == null ||
            !command.Payload.TryGetValue("question", out var question) ||
            string.IsNullOrWhiteSpace(question))
        {
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Parâmetro 'question' não informado no payload.");
        }

        // Nao exige GeminiApiKey especificamente: com o fallback automatico
        // (BrainProviderFactory), _brainQuery pode estar respondendo via Ollama local
        // mesmo sem nenhuma chave Gemini configurada. A checagem que importa e a de baixo.
        if (_brainQuery == null)
        {
            _logger?.LogWarning("Comando AskVault rejeitado: IVaultBrainQuery não configurado.");
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Consulta ao cofre não configurada nesta sessão.");
        }

        if (string.IsNullOrWhiteSpace(config.VaultPath) || !Directory.Exists(config.VaultPath))
        {
            _logger?.LogWarning("Comando AskVault rejeitado: Cofre local não configurado ou inexistente em '{VaultPath}'", config.VaultPath);
            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Rejected,
                "Cofre local não configurado ou diretório não encontrado.");
        }

        try
        {
            _logger?.LogInformation("Executando processamento de chat/captura no cofre para: \"{Question}\"", question);
            var sw = Stopwatch.StartNew();
            var outcome = await _brainQuery.ProcessChatTurnAsync(question.Trim(), config.VaultPath, ct);
            sw.Stop();

            var returnMessage = outcome.ReplyMessage;

            if (!string.IsNullOrWhiteSpace(outcome.SavedNotePath))
            {
                var noteTitle = Path.GetFileNameWithoutExtension(outcome.SavedNotePath);
                var savedNoteBadge = $"💾 Salvo em: [[{noteTitle}]]";
                returnMessage = string.IsNullOrWhiteSpace(returnMessage)
                    ? savedNoteBadge
                    : $"{returnMessage}\n\n{savedNoteBadge}";
            }

            if (outcome.Sources != null && outcome.Sources.Count > 0)
            {
                var sourcesFormatted = string.Join(", ", outcome.Sources.Select(s => string.IsNullOrWhiteSpace(s.Title) ? $"[[{Path.GetFileNameWithoutExtension(s.RelativePath)}]]" : $"[[{s.Title}]]"));
                returnMessage = string.IsNullOrWhiteSpace(returnMessage)
                    ? $"Fontes: {sourcesFormatted}"
                    : $"{returnMessage}\n\nFontes: {sourcesFormatted}";
            }

            SynapseActivityLogger.Instance.SetVaultPath(config.VaultPath);
            _ = SynapseActivityLogger.Instance.LogChatAsync(
                question.Trim(),
                returnMessage,
                sw.ElapsedMilliseconds,
                "Success",
                outcome.Sources?.Select(s => s.Title).ToList(),
                outcome.SavedNotePath);

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Success,
                returnMessage);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao processar consulta AskVault para o comando {CommandId}", command.Id);
            _ = SynapseActivityLogger.Instance.LogChatAsync(
                question.Trim(),
                string.Empty,
                0,
                "Failed",
                null,
                null,
                ex.Message);

            return new RemoteCommandResult(
                command.Id,
                DateTimeOffset.UtcNow,
                RemoteCommandStatus.Failed,
                $"Falha ao consultar o cofre com a IA: {ex.Message}");
        }
    }

    private static bool IsProcessAllowed(string processName, SynapseConfig config)
    {
        if (config.RemoteAllowedApps == null || config.RemoteAllowedApps.Count == 0)
        {
            return false;
        }

        var cleanProcess = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();

        foreach (var kvp in config.RemoteAllowedApps)
        {
            if (string.Equals(kvp.Key.Trim(), cleanProcess, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var valClean = Path.GetFileNameWithoutExtension(kvp.Value?.Trim() ?? string.Empty);
            if (string.Equals(valClean, cleanProcess, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Value?.Trim(), processName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
}
