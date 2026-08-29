using System.Drawing.Drawing2D;
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
        Text = "Synapse — Revisão Ativa (Flashcards & SM-2) [Pixel Edition]";
        Size = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 65,
            BackColor = SynapseTheme.Surface,
            Padding = new Padding(0)
        };
        pnlHeader.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            using var penDark = new Pen(SynapseTheme.Border, 2);
            e.Graphics.DrawLine(penDark, 0, pnlHeader.Height - 2, pnlHeader.Width, pnlHeader.Height - 2);
            using var penLight = new Pen(SynapseTheme.BorderLight, 1);
            e.Graphics.DrawLine(penLight, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
        };

        var accent = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(6, 65),
            BackColor = SynapseTheme.AccentPrimary,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        _lblCounter = new Label
        {
            Text = "► CARD 1 DE 1",
            Font = SynapseTheme.FontHeadline(8.5f),
            ForeColor = SynapseTheme.TextPrimary,
            Location = new Point(16, 12),
            AutoSize = true
        };

        _lblSource = new Label
        {
            Text = "Origem: Notas/Arquitetura.md",
            Font = SynapseTheme.FontCaption(8f),
            ForeColor = SynapseTheme.TextSecondary,
            Location = new Point(16, 38),
            AutoSize = true
        };

        pnlHeader.Controls.Add(accent);
        pnlHeader.Controls.Add(_lblCounter);
        pnlHeader.Controls.Add(_lblSource);
        Controls.Add(pnlHeader);

        // Question Panel (RPG Item / Quest Card Style)
        var pnlCard = new RoundedPanel
        {
            Location = new Point(24, 78),
            Size = new Size(656, 310),
            BackColor = SynapseTheme.SurfaceAlt,
            BorderColor = SynapseTheme.BorderLight,
            Padding = new Padding(20)
        };

        _lblQuestion = new Label
        {
            Location = new Point(20, 20),
            Size = new Size(616, 110),
            Font = SynapseTheme.FontHeadline(10.5f),
            ForeColor = SynapseTheme.TextPrimary,
            Text = "Carregando pergunta..."
        };

        var sep = new Panel
        {
            Location = new Point(20, 138),
            Size = new Size(616, 2),
            BackColor = SynapseTheme.Border
        };

        _lblAnswer = new Label
        {
            Location = new Point(20, 150),
            Size = new Size(616, 140),
            Font = SynapseTheme.FontBody(9f),
            ForeColor = SynapseTheme.NeonGreen,
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
            Text = "► Mostrar Resposta (Espaço)",
            Location = new Point(220, 410),
            Size = new Size(280, 42),
            Variant = SynapseButtonVariant.Primary
        };
        _btnReveal.Click += (_, _) => RevealAnswer();
        Controls.Add(_btnReveal);

        // Rating Buttons Panel (Hidden until reveal)
        _pnlRatings = new Panel
        {
            Location = new Point(24, 404),
            Size = new Size(656, 60),
            Visible = false
        };

        var btnAgain = CreateRatingButton("✖ Errei (0)", SynapseTheme.Error, 0, () => GradeCard(0));
        var btnHard = CreateRatingButton("▲ Difícil (3)", SynapseTheme.Warning, 168, () => GradeCard(3));
        var btnGood = CreateRatingButton("● Bom (4)", SynapseTheme.AccentPrimary, 336, () => GradeCard(4));
        var btnEasy = CreateRatingButton("★ Fácil (5)", SynapseTheme.NeonGreen, 504, () => GradeCard(5));

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
            Location = new Point(left, 6),
            Size = new Size(150, 42),
            FillOverride = color,
            Font = SynapseTheme.FontHeadline(8f)
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
        _lblCounter.Text = $"► CARD {_currentIndex + 1} DE {_cards.Count}";
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
