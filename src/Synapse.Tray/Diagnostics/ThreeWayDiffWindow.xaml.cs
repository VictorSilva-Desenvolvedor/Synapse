using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Synapse.Conflict.Diff;
using Synapse.Sync.Diagnostics;
using Synapse.Tray.UI;

namespace Synapse.Tray.Diagnostics;

/// <summary>
/// Resolucao de conflito lado a lado (local / base / remoto) com previa mesclada editavel.
///
/// A versao herdada do WinForms tinha tres defeitos, todos corrigidos aqui:
///
/// 1. A BASE era sempre vazia — o codigo declarava `var baseContent = string.Empty`.
///    Ela existe: o SyncBaseCache guarda a ultima versao sincronizada em
///    %LOCALAPPDATA%\Synapse\base_cache, que e exatamente a base do merge de 3 vias.
///
/// 2. O caminho da nota era deduzido de "Nota.conflito-{ts}.md", formato que o
///    SyncQueueProcessor nunca gravou — ele usa _conflitos/{nota.md}/local-{ts}.md.
///
/// 3. "Remoto" era sempre o arquivo clicado, entao clicar no local-*.md invertia os
///    dois lados da comparacao.
/// </summary>
public partial class ThreeWayDiffWindow : PixelWindow
{
    private readonly string _vaultRootPath;
    private readonly ConflictSources? _sources;

    private IReadOnlyList<DiffBlock> _blocks = [];

    public ThreeWayDiffWindow(string vaultRootPath, string conflictFilePath, string? baseCacheRoot = null)
    {
        _vaultRootPath = vaultRootPath;
        _sources = ConflictSetResolver.Resolve(vaultRootPath, conflictFilePath, baseCacheRoot);

        InitializeComponent();

        LoadDiffContents();
    }

    private void LoadDiffContents()
    {
        if (_sources is null)
        {
            Subtitle = "Conflito nao reconhecido";
            TargetPathText.Text = "Nao foi possivel localizar as versoes deste conflito.";
            LocalText.Text = string.Empty;
            RemoteText.Text = string.Empty;
            SetBaseMissing("As versoes local e remota nao foram encontradas na pasta do conflito.");
            return;
        }

        Subtitle = _sources.TargetRelativePath;
        TargetPathText.Text = $"Arquivo em conflito: {_sources.TargetRelativePath}";

        try
        {
            var localContent = ReadOrEmpty(_sources.LocalPath);
            var remoteContent = ReadOrEmpty(_sources.RemotePath);
            var baseContent = _sources.BasePath is null ? string.Empty : ReadOrEmpty(_sources.BasePath);

            LocalText.Text = localContent;
            RemoteText.Text = remoteContent;

            if (_sources.BasePath is null)
            {
                // Acontece de verdade quando o conflito surge no primeiro sync da nota:
                // nao existe versao comum anterior. Dizer isso e melhor que um painel vazio.
                SetBaseMissing("Sem versao base: este conflito surgiu antes da primeira sincronizacao bem-sucedida desta nota. O merge compara apenas os dois lados.");
            }
            else
            {
                BaseScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                BaseText.Text = baseContent;
                BaseText.Foreground = (Brush)FindResource("TextPrimaryBrush");
                BaseTitle.Text = "BASE (ULTIMO SYNC)";
                BaseTitle.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }

            _blocks = new ThreeWayDiffCalculator().Calculate(baseContent, localContent, remoteContent);
            UpdateMergedPreview();
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Erro ao carregar conteudo de conflito: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
    }

    private void SetBaseMissing(string message)
    {
        // Sem isto o ScrollViewer da largura infinita ao TextBlock e o TextWrapping
        // nunca age: a explicacao sairia numa linha so, cortada pela direita.
        BaseScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        BaseTitle.Text = "BASE INDISPONIVEL";
        BaseTitle.Foreground = (Brush)FindResource("WarningBrush");
        BaseText.Text = message;
        BaseText.Foreground = (Brush)FindResource("TextDisabledBrush");
    }

    private static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private void OnAcceptLocal(object sender, RoutedEventArgs e) => AcceptAll(BlockResolutionChoice.Local);

    private void OnAcceptRemote(object sender, RoutedEventArgs e) => AcceptAll(BlockResolutionChoice.Remote);

    private void OnKeepBoth(object sender, RoutedEventArgs e) => AcceptAll(BlockResolutionChoice.Both);

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
        => MergedBox.Text = ThreeWayDiffCalculator.BuildMergedResult(_blocks);

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_sources is null)
        {
            return;
        }

        try
        {
            var finalContent = MergedBox.Text;
            var targetFullPath = Path.Combine(_vaultRootPath, _sources.TargetRelativePath);

            var targetDir = Path.GetDirectoryName(targetFullPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            await File.WriteAllTextAsync(targetFullPath, finalContent);

            // Remove as duas versoes preservadas, nao so a que foi clicada — senao a
            // outra continuaria listada como conflito aberto.
            DeleteIfExists(_sources.LocalPath);
            DeleteIfExists(_sources.RemotePath);
            RemoveEmptyConflictDir(_sources.LocalPath);

            PixelMessageBox.Show(
                "Conflito resolvido.\nO arquivo foi atualizado e as versoes preservadas foram removidas.",
                "SYNAPSE",
                PixelMessageKind.Success,
                this);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            PixelMessageBox.Show($"Falha ao salvar resolucao: {ex.Message}", "ERRO", PixelMessageKind.Error, this);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void RemoveEmptyConflictDir(string anyFileInDir)
    {
        var dir = Path.GetDirectoryName(anyFileInDir);
        if (dir is not null && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
        {
            Directory.Delete(dir);
        }
    }
}
