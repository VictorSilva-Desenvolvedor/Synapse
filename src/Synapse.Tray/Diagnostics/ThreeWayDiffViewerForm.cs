using Synapse.Conflict.Diff;
using Synapse.Tray.UI;

namespace Synapse.Tray.Diagnostics;

/// <summary>
/// Janela visual de resolução de conflitos de merge 3-vias (Local / Base / Remoto) (V2.1, US-CONFLICT.5).
/// </summary>
public sealed class ThreeWayDiffViewerForm : Form
{
    private readonly string _vaultRootPath;
    private readonly string _conflictFilePath;
    private readonly string _targetRelativePath;

    private readonly RichTextBox _txtLocal;
    private readonly RichTextBox _txtBase;
    private readonly RichTextBox _txtRemote;
    private readonly RichTextBox _txtMergedPreview;

    private IReadOnlyList<DiffBlock> _blocks = [];

    public ThreeWayDiffViewerForm(string vaultRootPath, string conflictFilePath)
    {
        _vaultRootPath = vaultRootPath;
        _conflictFilePath = conflictFilePath;

        // Deduz o caminho relativo original (ex: _conflitos/Notas/Diario.conflito-20260827.md -> Notas/Diario.md)
        _targetRelativePath = DeduceTargetRelativePath(vaultRootPath, conflictFilePath);

        Text = $"Synapse — Resolução de Conflito: {_targetRelativePath}";
        Size = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        SynapseTheme.ApplyFormChrome(this);

        // Header Panel
        var pnlHeader = SynapseTheme.CreateHeaderBar(
            $"Resolução Visual de Conflito — {_targetRelativePath}",
            "Selecione a resolução desejada para os blocos ou edite diretamente no painel de resultado final.",
            60);
        Controls.Add(pnlHeader);

        // Main Table Layout (Top: 3 panels Side by Side; Bottom: Merged Result)
        var tableLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 3,
            Padding = new Padding(12),
            BackColor = SynapseTheme.Background
        };
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

        // 1. Local Panel
        var pnlLocal = CreateTextPanel("Versão Local (Seu Cofre)", out _txtLocal, SynapseTheme.AccentPrimary);
        tableLayout.Controls.Add(pnlLocal, 0, 0);

        // 2. Base Panel
        var pnlBase = CreateTextPanel("Versão Base (Último Sync)", out _txtBase, SynapseTheme.BorderStrong);
        tableLayout.Controls.Add(pnlBase, 1, 0);

        // 3. Remote Panel
        var pnlRemote = CreateTextPanel("Versão Remota (GitHub)", out _txtRemote, SynapseTheme.AccentSecondary);
        tableLayout.Controls.Add(pnlRemote, 2, 0);

        // 4. Result Preview Panel (spans all 3 columns)
        var pnlResult = CreateTextPanel("Resultado Mesclado Final (editável)", out _txtMergedPreview, SynapseTheme.Warning, isReadOnly: false);
        tableLayout.SetColumnSpan(pnlResult, 3);
        tableLayout.Controls.Add(pnlResult, 0, 1);

        Controls.Add(tableLayout);

        // Footer Actions Panel
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            BackColor = SynapseTheme.SurfaceAlt,
            Padding = new Padding(16, 10, 16, 10)
        };

        var btnAcceptLocal = new SynapseButton
        {
            Text = "Aceitar Tudo Local",
            Location = new Point(16, 10),
            Width = 140,
            Height = 32,
            Variant = SynapseButtonVariant.Secondary
        };
        btnAcceptLocal.Click += (_, _) => AcceptAll(BlockResolutionChoice.Local);

        var btnAcceptRemote = new SynapseButton
        {
            Text = "Aceitar Tudo Remoto",
            Location = new Point(165, 10),
            Width = 150,
            Height = 32,
            Variant = SynapseButtonVariant.Secondary
        };
        btnAcceptRemote.Click += (_, _) => AcceptAll(BlockResolutionChoice.Remote);

        var btnKeepBoth = new SynapseButton
        {
            Text = "Manter Ambos",
            Location = new Point(325, 10),
            Width = 120,
            Height = 32,
            Variant = SynapseButtonVariant.Secondary
        };
        btnKeepBoth.Click += (_, _) => AcceptAll(BlockResolutionChoice.Both);

        var btnSaveAndResolve = new SynapseButton
        {
            Text = "Salvar e Concluir Resolução",
            Location = new Point(870, 10),
            Width = 200,
            Height = 35,
            Variant = SynapseButtonVariant.Primary,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnSaveAndResolve.Click += async (_, _) => await SaveAndResolveAsync();

        pnlFooter.Controls.Add(btnAcceptLocal);
        pnlFooter.Controls.Add(btnAcceptRemote);
        pnlFooter.Controls.Add(btnKeepBoth);
        pnlFooter.Controls.Add(btnSaveAndResolve);
        Controls.Add(pnlFooter);

        Shown += (_, _) => LoadDiffContents();
    }

    private static Panel CreateTextPanel(string title, out RichTextBox textBox, Color accentColor, bool isReadOnly = true)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4), BackColor = SynapseTheme.Background };
        var accentBar = new Panel { Dock = DockStyle.Top, Height = 3, BackColor = accentColor };
        var lbl = new Label
        {
            Text = title,
            Font = SynapseTheme.FontBodyBold(9f),
            ForeColor = SynapseTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(0, 4, 0, 0)
        };

        textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = isReadOnly,
            BackColor = SynapseTheme.SurfaceAlt,
            ForeColor = SynapseTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = SynapseTheme.FontMono(9.5f),
            WordWrap = false
        };

        panel.Controls.Add(textBox);
        panel.Controls.Add(lbl);
        panel.Controls.Add(accentBar);
        return panel;
    }

    private void LoadDiffContents()
    {
        try
        {
            var localFullPath = Path.Combine(_vaultRootPath, _targetRelativePath);
            var localContent = File.Exists(localFullPath) ? File.ReadAllText(localFullPath) : string.Empty;
            var remoteContent = File.Exists(_conflictFilePath) ? File.ReadAllText(_conflictFilePath) : string.Empty;
            var baseContent = string.Empty; // Base anterior se disponível, ou vazio

            _txtLocal.Text = localContent;
            _txtBase.Text = baseContent;
            _txtRemote.Text = remoteContent;

            var calculator = new ThreeWayDiffCalculator();
            _blocks = calculator.Calculate(baseContent, localContent, remoteContent);

            UpdateMergedPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar conteúdo de conflito: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AcceptAll(BlockResolutionChoice choice)
    {
        foreach (var block in _blocks)
        {
            block.Choice = choice;
            block.CustomText = null;
        }

        UpdateMergedPreview();
    }

    private void UpdateMergedPreview()
    {
        var merged = ThreeWayDiffCalculator.BuildMergedResult(_blocks);
        _txtMergedPreview.Text = merged;
    }

    private async Task SaveAndResolveAsync()
    {
        try
        {
            var finalContent = _txtMergedPreview.Text;
            var targetFullPath = Path.Combine(_vaultRootPath, _targetRelativePath);

            var targetDir = Path.GetDirectoryName(targetFullPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // 1. Grava o resultado no caminho original
            await File.WriteAllTextAsync(targetFullPath, finalContent);

            // 2. Remove o arquivo de conflito preservado em _conflitos/
            if (File.Exists(_conflictFilePath))
            {
                File.Delete(_conflictFilePath);
            }

            MessageBox.Show("Conflito resolvido com sucesso!\nO arquivo foi atualizado e o registro de conflito removido.", "Synapse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao salvar resolução: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string DeduceTargetRelativePath(string vaultRootPath, string conflictFilePath)
    {
        var relative = Path.GetRelativePath(vaultRootPath, conflictFilePath).Replace('\\', '/');

        // Remove o prefixo _conflitos/ se presente
        if (relative.StartsWith("_conflitos/", StringComparison.OrdinalIgnoreCase))
        {
            relative = relative["_conflitos/".Length..];
        }

        // Remove sufixo .conflito-TIMESTAMP se houver (ex: Nota.conflito-20260827.md -> Nota.md)
        var fileName = Path.GetFileName(relative);
        var dir = Path.GetDirectoryName(relative);

        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"^(.*)\.conflito-[^.]+(\..*)$");
        if (match.Success)
        {
            var cleanFileName = match.Groups[1].Value + match.Groups[2].Value;
            return string.IsNullOrEmpty(dir) ? cleanFileName : Path.Combine(dir, cleanFileName).Replace('\\', '/');
        }

        return relative;
    }
}
