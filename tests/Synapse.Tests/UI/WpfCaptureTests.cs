using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Synapse.Tray.UI;
using Synapse.Agent.Models;
using Synapse.Brain.SpacedRepetition;
using Synapse.Tray.Agent;
using Synapse.Tray.QuickCapture;
using Synapse.Tray.Chat;
using Synapse.Tray.Diagnostics;
using Synapse.Tray.Onboarding;
using Synapse.Tray.Metrics;
using Synapse.Tray.Review;
using Synapse.Tray.RemoteApps;
using Xunit;

namespace Synapse.Tests.UI;

/// <summary>
/// Captura visual das telas WPF, uma por tela, para o ciclo do agente pixel-art-frontend.
/// Substitui FlaUiCaptureTests conforme cada janela e portada de WinForms para WPF.
/// </summary>
[Collection(WpfCaptureCollection.Name)]
public sealed class WpfCaptureTests
{
    private readonly WpfAppFixture _fixture;

    public WpfCaptureTests(WpfAppFixture fixture) => _fixture = fixture;

    private const string CapturaExemplo =
        "Ideia para o modulo de reconciliacao:\n\n" +
        "- Three-way diff com fallback para snapshot local\n" +
        "- Telemetria de conflitos resolvidos pelo usuario\n" +
        "- Sync em background a cada 60 segundos\n\n" +
        "#arquitetura #segundocerebro #sync";

    /// <summary>Estado padrao: a barra de comando, duas linhas de altura.</summary>
    [Fact]
    public void Captura_QuickCaptureWindow_Recolhida()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new QuickCaptureWindow(),
            "03a_QuickCapture_Recolhida.png",
            window => ((QuickCaptureWindow)window).InputText =
                "Reconciliacao three-way com fallback para snapshot local #arquitetura");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_AllowedAppsWindow()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new AllowedAppsWindow(),
            "99_AllowedAppsWindow.png");

        Assert.True(File.Exists(path));
    }

    /// <summary>Depois de clicar na seta: a bancada, com o painel de destino.</summary>
    [Fact]
    public void Captura_QuickCaptureWindow_Expandida()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new QuickCaptureWindow(),
            "03b_QuickCapture_Expandida.png",
            window =>
            {
                var w = (QuickCaptureWindow)window;
                w.InputText = CapturaExemplo;
                w.IsExpanded = true;
            });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_FlashcardReviewWindow()
    {
        var cards = new List<FlashcardItem>
        {
            new()
            {
                Id = "card-1",
                Question = "Qual e o objetivo principal do Synapse?",
                Answer = "Sincronizacao bidirecional offline-first entre Obsidian e GitHub, "
                         + "com IA local e Segundo Cerebro.",
                SourceNotePath = "Notas/Arquitetura.md",
                State = new Sm2State { IntervalDays = 3, RepetitionNumber = 2, EaseFactor = 2.5f }
            }
        };

        // Modo foco tem dois estados bem diferentes, e ambos precisam ser pontuados.
        var perguntando = WpfScreenshot.Capture(
            _fixture,
            () => new FlashcardReviewWindow(cards),
            "04a_Flashcard_Perguntando.png");

        var respondido = WpfScreenshot.Capture(
            _fixture,
            () => new FlashcardReviewWindow(cards),
            "04b_Flashcard_Respondido.png",
            window => ((FlashcardReviewWindow)window).RevealAnswer());

        Assert.True(File.Exists(perguntando));
        Assert.True(File.Exists(respondido));
    }

    /// <summary>
    /// O ContextMenu da bandeja vive num Popup com HWND proprio e so existe enquanto
    /// aberto por clique real no icone — RenderTargetBitmap nao alcanca. Esta janela de
    /// prova reproduz a moldura do ContextMenu e hospeda MenuItem de verdade, entao a
    /// aparencia pontuada e a mesma: todo o visual vem dos estilos de PixelTheme.xaml.
    /// </summary>
    [Fact]
    public void Captura_MenuDaBandeja()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () =>
            {
                var win = new PixelWindow
                {
                    Title = "MENU DA BANDEJA",
                    Subtitle = "Janela de prova - o menu real vive num Popup.",
                    Width = 400,
                    SizeToContent = System.Windows.SizeToContent.Height
                };

                var items = new System.Windows.Controls.StackPanel { Margin = new Thickness(4) };

                void Item(string header, bool isChecked = false)
                {
                    var mi = new System.Windows.Controls.MenuItem { Header = header };
                    if (isChecked)
                    {
                        mi.IsChecked = true;
                    }
                    items.Children.Add(mi);
                }

                void Sep() => items.Children.Add(new System.Windows.Controls.Separator());

                // O painel e a MESMA classe que o menu real usa — se fosse uma copia,
                // a tela pontuada deixaria de ser a tela entregue.
                var panel = new TrayMenuPanel();
                panel.SetStatus(TrayStatusKind.Ok, "Sincronizado", "ultimo sync 14:32 · 0 pendentes");
                items.Children.Add(panel.AsMenuItem());

                Sep();
                Item("Pausar Sincronizacao");
                Item("Reconectar GitHub");
                Item("Controle Remoto: Ativado", isChecked: true);
                Sep();
                Item("Diagnostico e Conflitos...");
                Item("Configuracoes...");
                Item("Abrir Pasta de Logs");
                Sep();
                Item("Sair da Bandeja");

                // Mesma moldura do template de ContextMenu: bevel 8-bit sobre Surface.
                var outer = new Border
                {
                    Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("SurfaceBrush"),
                    BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("EdgeLightBrush"),
                    BorderThickness = new Thickness(2, 2, 0, 0),
                    Margin = new Thickness(16),
                    Child = new Border
                    {
                        BorderBrush = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("VoidBrush"),
                        BorderThickness = new Thickness(0, 0, 2, 2),
                        Child = items
                    }
                };

                win.Content = outer;
                return win;
            },
            "09_MenuDaBandeja.png");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_ChatVaultWindow()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new ChatVaultWindow(),
            "02_ChatVaultWindow.png",
            window => ((ChatVaultWindow)window).SetSampleConversation(
                [
                    ChatMessage.Assistant("Ola! Pode me contar o que quiser guardar, ou perguntar sobre o seu cofre."),
                    ChatMessage.User("Como funciona a repeticao espacada SM-2 no Synapse?"),
                    ChatMessage.Assistant(
                        "O SM-2 calcula intervalos progressivos (1 dia, 6 dias, ...) e ajusta o fator de "
                        + "facilidade conforme a sua nota de 0 a 5. Ele prioriza notas marcadas com #flashcard.",
                        [
                            new ChatSource("Arquitetura.md", "92%", string.Empty),
                            new ChatSource("Flashcards.md", "87%", string.Empty),
                            new ChatSource("Roadmap.md", "71%", string.Empty)
                        ]),
                    ChatMessage.System("Salvo em: [[SM-2 no Synapse]] (Notas/SM-2 no Synapse.md)")
                ],
                "Pronto para conversar com o seu Segundo Cerebro."));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_VaultStatsWindow()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new VaultStatsWindow(),
            "05_VaultStatsWindow.png",
            window => ((VaultStatsWindow)window).SetSampleData(
                "1.284", "412.905", "~1.376 min", "37",
                [
                    // A fracao da barra e relativa a maior categoria (312 = 1,0).
                    new CategoryRow("Arquitetura", "312", "24,3%", 1.00),
                    new CategoryRow("Segundo Cerebro", "268", "20,9%", 0.86),
                    new CategoryRow("Diario", "241", "18,8%", 0.77),
                    new CategoryRow("Referencias", "196", "15,3%", 0.63),
                    new CategoryRow("Projetos", "154", "12,0%", 0.49),
                    new CategoryRow("Inbox", "113", "8,8%", 0.36)
                ]));

        Assert.True(File.Exists(path));
    }

    /// <summary>Risco alto: alguem digitando no seu teclado. O caso mais grave.</summary>
    [Fact]
    public void Captura_RemoteConfirmation_RiscoAlto()
    {
        var command = new RemoteCommand(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            RemoteCommandType.TypeText,
            new Dictionary<string, string>
            {
                ["processName"] = "Obsidian",
                ["text"] = "[[Arquitetura Pixel Art]] adicionada ao mapa mental."
            },
            "Pixel-Terminal-Remote");

        var path = WpfScreenshot.Capture(
            _fixture,
            () => new RemoteConfirmationWindow(command, TimeSpan.FromSeconds(30)),
            "08a_RemoteConfirmation_RiscoAlto.png");

        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// Risco baixo: abrir uma nota. Antes este tipo caia no fallback e exibia apenas
    /// "Tipo: OpenNote", sem dizer qual nota.
    /// </summary>
    [Fact]
    public void Captura_RemoteConfirmation_RiscoBaixo()
    {
        var command = new RemoteCommand(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(-12),
            RemoteCommandType.OpenNote,
            new Dictionary<string, string> { ["relativePath"] = "Notas/Arquitetura.md" },
            "Pixel-Terminal-Remote");

        var path = WpfScreenshot.Capture(
            _fixture,
            () => new RemoteConfirmationWindow(command, TimeSpan.FromSeconds(30)),
            "08b_RemoteConfirmation_RiscoBaixo.png");

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_OnboardingWindow()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new OnboardingWindow(),
            "01_OnboardingWindow.png",
            window => ((OnboardingWindow)window).SetSampleData(
                "seu-usuario",
                "meu-cofre",
                @"D:\Obsidian\Segundo Cerebro",
                "Token valido e autenticado.",
                tokenOk: true));

        Assert.True(File.Exists(path));
    }

    private static readonly string LogExemplo = string.Join(Environment.NewLine,
    [
        "[20:31:02 INF] Sincronizacao iniciada (ciclo #4821)",
        "[20:31:02 INF] Varredura do cofre: 1284 notas, 37 alteradas",
        "[20:31:03 WRN] Conflito detectado em Notas/Diario-2026-08-28.md",
        "[20:31:03 INF] Conflito preservado em _conflitos/ (RNF-2: zero perda)",
        "[20:31:04 INF] Push concluido: 36 arquivos enviados",
        "[20:31:04 INF] Sincronizacao concluida em 2,1s"
    ]);

    private static readonly ConflictRow[] ConflitosExemplo =
    [
        new("Diario-2026-08-28.md", "Notas/Diario-2026-08-28.md", "28/08/2026 20:30", "1,4 KB", string.Empty),
        new("Arquitetura.md", "Docs/Arquitetura.md", "27/08/2026 18:45", "8,2 KB", string.Empty),
        new("Roadmap.md", "Projetos/Roadmap.md", "27/08/2026 09:12", "3,7 KB", string.Empty)
    ];

    /// <summary>O seletor de conflitos, aberto pelo aviso do Diagnostico.</summary>
    [Fact]
    public void Captura_ConflictPickerWindow()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new ConflictPickerWindow(@"C:\Cofre", ConflitosExemplo),
            "06c_ConflictPicker.png");

        Assert.True(File.Exists(path));
    }

    /// <summary>O estado bom: nenhum conflito, aviso verde de uma linha.</summary>
    [Fact]
    public void Captura_DiagnosticsWindow_Limpo()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new DiagnosticsWindow(),
            "06b_Diagnostics_Limpo.png",
            window => ((DiagnosticsWindow)window).SetSampleData([], LogExemplo));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_DiagnosticsWindow()
    {
        var path = WpfScreenshot.Capture(
            _fixture,
            () => new DiagnosticsWindow(),
            "06a_Diagnostics_Conflitos.png",
            window => ((DiagnosticsWindow)window).SetSampleData(ConflitosExemplo, LogExemplo));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Captura_ThreeWayDiffWindow()
    {
        // Layout REAL que o SyncQueueProcessor grava: _conflitos/{nota.md}/local-{ts}.md
        // e remoto-{ts}.md, com a base fora do cofre, no cache do SyncBaseCache.
        var tempDir = Path.Combine(Path.GetTempPath(), "Synapse_Diff_" + Guid.NewGuid().ToString("N")[..8]);
        var conflictDir = Path.Combine(tempDir, "_conflitos", "Notas", "MinhaIdeia.md");
        var baseCache = Path.Combine(tempDir, "__base_cache");
        Directory.CreateDirectory(conflictDir);
        Directory.CreateDirectory(Path.Combine(baseCache, "Notas"));

        var localFile = Path.Combine(conflictDir, "local-20260828-203100.md");
        var remoteFile = Path.Combine(conflictDir, "remoto-20260828-203100.md");
        var baseFile = Path.Combine(baseCache, "Notas", "MinhaIdeia.md");

        File.WriteAllText(baseFile,
            "# Minha Ideia\n\nRascunho inicial da reconciliacao.\n\n- Comparar duas versoes");
        File.WriteAllText(localFile,
            "# Minha Ideia\n\nAlteracao feita no desktop.\n\n- Reconciliacao three-way\n- Snapshot local");
        File.WriteAllText(remoteFile,
            "# Minha Ideia\n\nAlteracao vinda do GitHub mobile.\n\n- Fila offline\n- Retry exponencial");

        try
        {
            var comBase = WpfScreenshot.Capture(
                _fixture,
                () => new ThreeWayDiffWindow(tempDir, localFile, baseCache),
                "07a_ThreeWayDiff_ComBase.png");

            // Estado real: conflito antes do primeiro sync bem-sucedido da nota, entao
            // nao ha versao comum anterior. O painel precisa explicar, nao ficar vazio.
            File.Delete(baseFile);
            var semBase = WpfScreenshot.Capture(
                _fixture,
                () => new ThreeWayDiffWindow(tempDir, localFile, baseCache),
                "07b_ThreeWayDiff_SemBase.png");

            Assert.True(File.Exists(comBase));
            Assert.True(File.Exists(semBase));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* limpeza best-effort */ }
        }
    }
}
