using Synapse.Brain.SpacedRepetition;
using Synapse.Tray.UI;

namespace Synapse.Tray.Review;

/// <summary>
/// Janela visual de Revisão de Flashcards com Repetição Espaçada SM-2 (V7.1).
/// </summary>
public sealed class FlashcardReviewForm : Form
{
    private readonly List<FlashcardItem> _cards;
    private int _currentIndex = 0;
    private readonly Label _lblCounter;
    private readonly Label _lblSource;
    private readonly Label _lblQuestion;
    private readonly Label _lblAnswer;
    private readonly SynapseButton _btnReveal;
    private readonly Panel _pnlRatings;

    public FlashcardReviewForm(IReadOnlyList<FlashcardItem>? cards = null)
    {
        _cards = cards?.ToList() ?? GenerateSampleCards();

        Text = "Synapse — Revisão Ativa (Flashcards & SM-2)";
        Size = new Size(680, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 55,
            BackColor = SynapseTheme.SurfaceAlt,
            Padding = new Padding(16, 12, 16, 12)
        };

        _lblCounter = new Label
        {
            Text = "Card 1 de 1",
            Font = SynapseTheme.FontBodyBold(10f),
            ForeColor = SynapseTheme.TextPrimary,
            Location = new Point(16, 16),
            AutoSize = true
        };

        _lblSource = new Label
        {
            Text = "Origem: Nota",
            Font = SynapseTheme.FontCaption(9f),
            ForeColor = SynapseTheme.TextSecondary,
            Location = new Point(200, 18),
            AutoSize = true
        };

        pnlHeader.Controls.Add(_lblCounter);
        pnlHeader.Controls.Add(_lblSource);
        Controls.Add(pnlHeader);

        // Question Panel
        var pnlCard = new RoundedPanel
        {
            Location = new Point(30, 80),
            Size = new Size(605, 300),
            BackColor = SynapseTheme.Surface,
            BorderColor = SynapseTheme.Border,
            Radius = SynapseTheme.RadiusLarge,
            Padding = new Padding(24)
        };

        _lblQuestion = new Label
        {
            Location = new Point(24, 24),
            Size = new Size(556, 100),
            Font = SynapseTheme.FontHeadline(13f),
            ForeColor = SynapseTheme.TextPrimary,
            Text = "Carregando pergunta..."
        };

        var sep = new Panel
        {
            Location = new Point(24, 130),
            Size = new Size(556, 1),
            BackColor = SynapseTheme.Border
        };

        _lblAnswer = new Label
        {
            Location = new Point(24, 146),
            Size = new Size(556, 140),
            Font = SynapseTheme.FontBody(11f),
            ForeColor = SynapseTheme.TextSecondary,
            Text = "...",
            Visible = false
        };

        pnlCard.Controls.Add(_lblQuestion);
        pnlCard.Controls.Add(sep);
        pnlCard.Controls.Add(_lblAnswer);
        Controls.Add(pnlCard);

        // Action Buttons
        _btnReveal = new SynapseButton
        {
            Text = "Mostrar Resposta (Espaço)",
            Location = new Point(230, 400),
            Size = new Size(220, 46),
            Variant = SynapseButtonVariant.Secondary,
            Radius = SynapseTheme.RadiusMedium
        };
        _btnReveal.Click += (_, _) => RevealAnswer();
        Controls.Add(_btnReveal);

        // Rating Buttons Panel (Hidden until reveal)
        _pnlRatings = new Panel
        {
            Location = new Point(30, 396),
            Size = new Size(605, 60),
            Visible = false
        };

        var btnAgain = CreateRatingButton("🔴 Errei (0)", SynapseTheme.Error, 0, () => GradeCard(0));
        var btnHard = CreateRatingButton("🟡 Difícil (3)", SynapseTheme.Warning, 155, () => GradeCard(3));
        var btnGood = CreateRatingButton("🟢 Bom (4)", SynapseTheme.AccentPrimary, 310, () => GradeCard(4));
        var btnEasy = CreateRatingButton("🔵 Fácil (5)", SynapseTheme.AccentSecondary, 465, () => GradeCard(5));

        _pnlRatings.Controls.Add(btnAgain);
        _pnlRatings.Controls.Add(btnHard);
        _pnlRatings.Controls.Add(btnGood);
        _pnlRatings.Controls.Add(btnEasy);
        Controls.Add(_pnlRatings);

        UpdateCardView();
    }

    private SynapseButton CreateRatingButton(string text, Color color, int left, Action onClick)
    {
        var btn = new SynapseButton
        {
            Text = text,
            Location = new Point(left, 5),
            Size = new Size(135, 46),
            FillOverride = color,
            Radius = SynapseTheme.RadiusMedium
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void RevealAnswer()
    {
        _lblAnswer.Visible = true;
        _btnReveal.Visible = false;
        _pnlRatings.Visible = true;
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
            MessageBox.Show("🎉 Parabéns! Você concluiu todas as revisões de hoje.", "Revisão Concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
            return;
        }

        UpdateCardView();
    }

    private void UpdateCardView()
    {
        if (_cards.Count == 0)
        {
            _lblQuestion.Text = "Nenhum flashcard disponível para revisão no momento.";
            _btnReveal.Enabled = false;
            return;
        }

        var card = _cards[_currentIndex];
        _lblCounter.Text = $"Card {_currentIndex + 1} de {_cards.Count}";
        _lblSource.Text = $"Origem: {Path.GetFileNameWithoutExtension(card.SourceNotePath)}";
        _lblQuestion.Text = card.Question;
        _lblAnswer.Text = card.Answer;

        _lblAnswer.Visible = false;
        _btnReveal.Visible = true;
        _pnlRatings.Visible = false;
    }

    private static List<FlashcardItem> GenerateSampleCards()
    {
        return
        [
            new FlashcardItem
            {
                SourceNotePath = "Arquitetura/Hexagonal.md",
                Question = "Qual é o principal objetivo da Arquitetura Hexagonal (Ports & Adapters)?",
                Answer = "Isolar a lógica de negócio pura (Domínio/Core) de frameworks, bancos e interfaces externas (Adapters)."
            },
            new FlashcardItem
            {
                SourceNotePath = "PKM/Zettelkasten.md",
                Question = "O que caracteriza uma Nota Atômica no método Zettelkasten?",
                Answer = "Uma nota que expressa uma única ideia autocontida, com título declarativo e wikilinks bidirecionais."
            }
        ];
    }
}
