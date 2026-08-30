namespace Synapse.Tray.Chat;

/// <summary>Quem falou. Define alinhamento e cor da bolha.</summary>
public enum ChatRole
{
    User,
    Assistant,
    System
}

/// <summary>
/// Uma nota do cofre citada numa resposta, como aparece nas fichas da bolha.
/// </summary>
public sealed record ChatSource(string Title, string Similarity, string FullPath)
{
    /// <summary>Rotulo da ficha: "Arquitetura.md 92%".</summary>
    public string Label => $"{Title} {Similarity}";
}

/// <summary>
/// Uma mensagem no historico do chat.
///
/// O WinForms montava o historico manipulando SelectionFont/SelectionColor de um
/// RichTextBox, o que deixava o trecho recem-inserido selecionado e pintava um realce
/// azul permanente sobre o texto. Modelar a mensagem como dado e deixar o ItemsControl
/// renderizar elimina a classe inteira desse bug.
///
/// As fontes vivem NA mensagem, nao numa tabela separada: a pergunta "de onde veio
/// isso?" e sobre uma resposta especifica, entao a resposta e que deve carregar a
/// procedencia.
/// </summary>
public sealed record ChatMessage(
    ChatRole Role,
    string Sender,
    string Text,
    DateTime At,
    IReadOnlyList<ChatSource>? Sources = null)
{
    public static ChatMessage User(string text) => new(ChatRole.User, "VOCE", text, DateTime.Now);

    public static ChatMessage Assistant(string text, IReadOnlyList<ChatSource>? sources = null)
        => new(ChatRole.Assistant, "SYNAPSE BRAIN", text, DateTime.Now, sources);

    public static ChatMessage System(string text) => new(ChatRole.System, "SISTEMA", text, DateTime.Now);

    /// <summary>Bolha transitoria enquanto a IA responde. Removida quando a resposta chega.</summary>
    public static ChatMessage Thinking() => new(ChatRole.Assistant, "SYNAPSE BRAIN", "PENSANDO...", DateTime.Now);

    public string Header => $"{Sender} - {At:HH:mm}";

    public bool HasSources => Sources is { Count: > 0 };
}
