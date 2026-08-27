using Synapse.Conflict.Diff;

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
    private readonly Label _lblInfo;

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
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(24, 24, 27)
        };

        var lblTitle = new Label
        {
            Text = $"Resolução Visual de Conflito — {_targetRelativePath}",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 10),
            AutoSize = true
        };

        _lblInfo = new Label
        {
            Text = "Selecione a resolução desejada para os blocos ou edite diretamente no painel de resultado final.",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(161, 161, 170),
            Location = new Point(20, 34),
            AutoSize = true
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(_lblInfo);
        Controls.Add(pnlHeader);

        // Main Table Layout (Top: 3 panels Side by Side; Bottom: Merged Result)
        var tableLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 3,
            Padding = new Padding(12)
        };
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

        // 1. Local Panel
        var pnlLocal = CreateTextPanel("Versão Local (Seu Cofre)", out _txtLocal, Color.FromArgb(236, 253, 245));
        tableLayout.Controls.Add(pnlLocal, 0, 0);

        // 2. Base Panel
        var pnlBase = CreateTextPanel("Versão Base (Último Sync)", out _txtBase, Color.FromArgb(244, 244, 245));
        tableLayout.Controls.Add(pnlBase, 1, 0);

        // 3. Remote Panel
        var pnlRemote = CreateTextPanel("Versão Remota (GitHub)", out _txtRemote, Color.FromArgb(239, 246, 255));
        tableLayout.Controls.Add(pnlRemote, 2, 0);

        // 4. Result Preview Panel (spans all 3 columns)
        var pnlResult = CreateTextPanel("Resultado Mesclado Final (Editável):", out _txtMergedPreview, Color.White, isReadOnly: false);
        tableLayout.SetColumnSpan(pnlResult, 3);
        tableLayout.Controls.Add(pnlResult, 0, 1);

        Controls.Add(tableLayout);

        // Footer Actions Panel
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            BackColor = Color.FromArgb(244, 244, 245),
            Padding = new Padding(16, 10, 16, 10)
        };

        var btnAcceptLocal = new Button
        {
            Text = "Aceitar Tudo Local",
            Location = new Point(16, 12),
            Width = 140,
            Height = 32
        };
        btnAcceptLocal.Click += (_, _) => AcceptAll(BlockResolutionChoice.Local);

        var btnAcceptRemote = new Button
        {
            Text = "Aceitar Tudo Remoto",
            Location = new Point(165, 12),
            Width = 150,
            Height = 32
        };
        btnAcceptRemote.Click += (_, _) => AcceptAll(BlockResolutionChoice.Remote);

        var btnKeepBoth = new Button
        {
            Text = "Manter Ambos",
            Location = new Point(325, 12),
            Width = 120,
            Height = 32
        };
        btnKeepBoth.Click += (_, _) => AcceptAll(BlockResolutionChoice.Both);

        var btnSaveAndResolve = new Button
        {
            Text = "Salvar e Concluir Resolução",
            Location = new Point(870, 10),
            Width = 200,
            Height = 35,
            BackColor = Color.FromArgb(16, 185, 129),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
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

    private static Panel CreateTextPanel(string title, out RichTextBox textBox, Color bgColor, bool isReadOnly = true)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        var lbl = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 22
        };

        textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = isReadOnly,
            BackColor = bgColor,
            Font = new Font("Consolas", 9.5f, FontStyle.Regular),
            WordWrap = false
        };

        panel.Controls.Add(textBox);
        panel.Controls.Add(lbl);
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
