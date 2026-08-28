using Synapse.Brain.Models;

namespace Synapse.Brain.Ports;

/// <summary>
/// Contrato para provedores de inteligência artificial do Synapse Brain.
/// </summary>
public interface IBrainAiProvider
{
    string ProviderName { get; }

    Task<AiStructuredNote> ProcessRawNoteAsync(
        string rawInput,
        IReadOnlyList<string> existingVaultNotes,
        CancellationToken ct = default);

    Task<string> GenerateMocAsync(
        string topic,
        IReadOnlyList<string> relatedNotes,
        CancellationToken ct = default);

    /// <summary>
    /// Envia um prompt de texto livre e devolve a resposta da IA em Markdown, sem
    /// forçar nenhum schema JSON. Usado para perguntas e respostas (RAG), onde não
    /// existe "nota estruturada" para produzir. Lança exceção se a IA não responder
    /// com sucesso — nunca deve devolver o próprio prompt de entrada como resposta.
    /// </summary>
    Task<string> AskQuestionAsync(string prompt, CancellationToken ct = default);

    /// <summary>
    /// Processa um turno de conversa com o Segundo Cérebro, decidindo dinamicamente se
    /// há informação para capturar como nota no cofre, se é uma pergunta a ser respondida via RAG,
    /// ambos ou apenas small talk / confirmação.
    /// </summary>
    Task<ChatTurnResult> ProcessChatTurnAsync(
        string userMessage,
        IReadOnlyList<string> existingVaultNotes,
        IReadOnlyList<string> existingCategoryFolders,
        IReadOnlyList<SemanticSearchResult> relatedNotes,
        CancellationToken ct = default);

    /// <summary>
    /// Executa um passo secundário de refinamento através da IA para filtrar qualquer ruído,
    /// metadados internos, resíduos de prompts ou contexto irrelevante, entregando apenas a
    /// resposta essencial, limpa e precisa para o usuário final em formato Markdown.
    /// </summary>
    Task<string> RefineAnswerAsync(
        string userQuestion,
        string rawDraft,
        CancellationToken ct = default) => Task.FromResult(rawDraft);
}
