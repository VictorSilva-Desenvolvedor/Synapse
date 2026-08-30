using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Synapse.Brain.SpacedRepetition;
using Synapse.Tray.UI;

namespace Synapse.Tray.Review;

/// <summary>
/// Revisao de Flashcards com Repeticao Espacada SM-2, em modo foco.
///
/// A tela nao tem chrome: sem barra de titulo, sem moldura de carta, sem rodape.
/// So a pergunta no centro. Espaco revela a resposta; 0/3/4/5 dao a nota.
///
/// Sem barra de titulo, duas saidas precisam existir de proposito: a janela inteira
/// arrasta (OnDragWindow) e ha um X discreto no topo, alem do Esc que PixelWindow ja
/// trata. Sem isso, quem usa mouse fica preso na tela.
/// </summary>
public partial class FlashcardReviewWindow : PixelWindow
{
    private readonly List<FlashcardItem> _cards;
    private int _currentIndex;
    private bool _revealed;

    public FlashcardReviewWindow(IReadOnlyList<FlashcardItem>? cards = null)
    {
        _cards = cards?.ToList() ?? GenerateSampleCards();

        InitializeComponent();
        UpdateCardView();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_revealed)
        {
            if (e.Key is Key.Space or Key.Enter)
            {
                RevealAnswer();
                e.Handled = true;
                return;
            }
        }
        else
        {
            var grade = e.Key switch
            {
                Key.D0 or Key.NumPad0 => 0,
                Key.D3 or Key.NumPad3 => 3,
                Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5,
                _ => -1
            };

            if (grade >= 0)
            {
                e.Handled = true;
                GradeCard(grade);
                return;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>Revela a resposta sem clique. Usado pelo harness de captura.</summary>
    public void RevealAnswer()
    {
        if (_revealed || _cards.Count == 0)
        {
            return;
        }

        _revealed = true;
        AnswerText.Visibility = Visibility.Visible;
        Separator.Visibility = Visibility.Visible;
        RevealHint.Visibility = Visibility.Collapsed;
        RatingPanel.Visibility = Visibility.Visible;
    }

    private void OnGrade(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var grade))
        {
            GradeCard(grade);
        }
    }

    private void GradeCard(int grade)
    {
        if (_currentIndex < _cards.Count)
        {
            var card = _cards[_currentIndex];
            card.State = Sm2Engine.Evaluate(card.State, grade, DateTimeOffset.UtcNow);
        }

        _currentIndex++;
        if (_currentIndex >= _cards.Count)
        {
            PixelMessageBox.Show(
                "Parabens! Voce concluiu todas as revisoes de hoje.",
                "REVISAO CONCLUIDA",
                PixelMessageKind.Success,
                this);
            Close();
            return;
        }

        UpdateCardView();
    }

    private void UpdateCardView()
    {
        _revealed = false;

        if (_cards.Count == 0)
        {
            QuestionText.Text = "Nenhum flashcard disponivel para revisao no momento.";
            CounterText.Text = "0 / 0";
            SourceText.Text = string.Empty;
            RevealHint.Text = "ESC PARA FECHAR";
            AnswerText.Visibility = Visibility.Collapsed;
            Separator.Visibility = Visibility.Collapsed;
            RatingPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var card = _cards[_currentIndex];
        CounterText.Text = $"{_currentIndex + 1} / {_cards.Count}";
        SourceText.Text = Path.GetFileNameWithoutExtension(card.SourceNotePath);
        QuestionText.Text = card.Question;
        AnswerText.Text = card.Answer;

        RevealHint.Text = "ESPACO PARA REVELAR";
        RevealHint.Visibility = Visibility.Visible;
        AnswerText.Visibility = Visibility.Collapsed;
        Separator.Visibility = Visibility.Collapsed;
        RatingPanel.Visibility = Visibility.Collapsed;
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static List<FlashcardItem> GenerateSampleCards()
    {
        return
        [
            new FlashcardItem
            {
                SourceNotePath = "Arquitetura/Hexagonal.md",
                Question = "Qual e o principal objetivo da Arquitetura Hexagonal (Ports & Adapters)?",
                Answer = "Isolar a logica de negocio pura (Dominio/Core) de frameworks, bancos e interfaces externas (Adapters)."
            },
            new FlashcardItem
            {
                SourceNotePath = "PKM/Zettelkasten.md",
                Question = "O que caracteriza uma Nota Atomica no metodo Zettelkasten?",
                Answer = "Uma nota que expressa uma unica ideia autocontida, com titulo declarativo e wikilinks bidirecionais."
            }
        ];
    }
}
