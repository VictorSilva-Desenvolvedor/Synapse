using System.Text;
using Synapse.Brain.Models;
using Synapse.Brain.Ports;

namespace Synapse.Brain.Services;

/// <summary>
/// Orquestrador de Captura Inteligente e Estruturação do Segundo Cérebro.
/// </summary>
public sealed class SmartCaptureService
{
    private readonly IBrainAiProvider _aiProvider;
    private readonly BrainConfig _config;

    public SmartCaptureService(IBrainAiProvider aiProvider, BrainConfig config)
    {
        _aiProvider = aiProvider;
        _config = config;
    }

    public async Task<string> ProcessAndSaveToVaultAsync(
        string rawInput,
        string vaultRootPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRootPath);

        // 1. Escaneia notas existentes no cofre para contextualização da IA
        var existingNotes = NoteFileWriter.GetVaultNoteTitles(vaultRootPath);

        // 2. Processa com o provedor de IA
        var structured = await _aiProvider.ProcessRawNoteAsync(rawInput, existingNotes, ct);

        // 3. Grava nota estruturada via NoteFileWriter compartilhado
        return await NoteFileWriter.WriteStructuredNoteAsync(structured, vaultRootPath, _config, existingNotes, ct);
    }
}
