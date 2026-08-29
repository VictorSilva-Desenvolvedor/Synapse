using System.Diagnostics;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using Synapse.Agent.Models;
using Synapse.Brain.Models;
using Synapse.Brain.SpacedRepetition;
using Synapse.Sync.Config;
using Synapse.Tray.Agent;
using Synapse.Tray.Chat;
using Synapse.Tray.Diagnostics;
using Synapse.Tray.Metrics;
using Synapse.Tray.Onboarding;
using Synapse.Tray.QuickCapture;
using Synapse.Tray.Review;
using Synapse.Tray.UI;
using Xunit;

namespace Synapse.Tests.UI;

/// <summary>
/// Testes de automação e captura visual de telas com FlaUI (UIA3).
/// </summary>
public sealed class FlaUiCaptureTests
{
    private static readonly string OutputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "screenshots"));
    private static readonly string BrainArtifactDir = @"C:\Users\victo\.gemini\antigravity-ide\brain\e89050b2-71d3-4191-a882-ce8a070b44df";

    static FlaUiCaptureTests()
    {
        Directory.CreateDirectory(OutputDir);
        if (Directory.Exists(BrainArtifactDir))
        {
            Directory.CreateDirectory(Path.Combine(BrainArtifactDir, "screenshots"));
        }
    }

    [Fact]
    public void FlaUI_CanCapture_OnboardingForm()
    {
        CaptureForm(
            () => new OnboardingForm(),
            "01_OnboardingForm.png",
            formSetup: null,
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Synapse", window.Title);
            });
    }

    [Fact]
    public void FlaUI_CanCapture_ChatVaultForm()
    {
        CaptureForm(
            () => new ChatVaultForm(),
            "02_ChatVaultForm.png",
            formSetup: form =>
            {
                if (form is ChatVaultForm chatForm)
                {
                    var method = typeof(ChatVaultForm).GetMethod("AppendMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(chatForm, ["Você", "Como funciona a repetição espaçada SM-2 no Synapse?"]);
                    method?.Invoke(chatForm, ["Synapse Brain", "O algoritmo SM-2 calcula intervalos progressivos (1 dia, 6 dias, etc.) e ajusta o fator de facilidade (Ease Factor) com base na sua avaliação (0 a 5). Ele prioriza notas marcadas com a tag #flashcard no seu cofre do Obsidian."]);

                    var txtInput = form.Controls.Find("_txtInput", true).FirstOrDefault() as System.Windows.Forms.TextBox;
                    if (txtInput != null) txtInput.Text = "Onde posso encontrar o arquivo de configuração do cofre?";
                }
            },
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Segundo Cérebro", window.Title);
            });
    }

    [Fact]
    public void FlaUI_CanCapture_QuickCaptureForm()
    {
        CaptureForm(
            () => new QuickCaptureForm(),
            "03_QuickCaptureForm.png",
            formSetup: form =>
            {
                var field = typeof(QuickCaptureForm).GetField("_txtInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(form) is System.Windows.Forms.RichTextBox txtInput)
                {
                    txtInput.Text = "💡 Ideia para o Módulo de Reconciliação:\n\n- Usar Three-Way Diff com fallback automático para snapshot local.\n- Registrar telemetria de conflitos resolvidos pelo usuário em logs/conflitos.json.\n- Sincronização em background a cada 60 segundos com o GitHub.\n\n#arquitetura #segundocérebro #synapse";
                }
            },
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Captura Rápida", window.Title);
            });
    }

    [Fact]
    public void FlaUI_CanCapture_FlashcardReviewForm()
    {
        var sampleCards = new List<FlashcardItem>
        {
            new()
            {
                Id = "card-1",
                Question = "Qual é o objetivo principal do Synapse?",
                Answer = "Sincronização bidirecional offline-first entre Obsidian e GitHub com IA local e Segundo Cérebro.",
                SourceNotePath = "Notas/Arquitetura.md",
                State = new Sm2State { IntervalDays = 3, RepetitionNumber = 2, EaseFactor = 2.5f }
            }
        };

        CaptureForm(
            () => new FlashcardReviewForm(sampleCards),
            "04_FlashcardReviewForm.png",
            formSetup: null,
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Revisão Ativa", window.Title);
            });
    }

    [Fact]
    public void FlaUI_CanCapture_VaultStatsForm()
    {
        CaptureForm(
            () => new VaultStatsForm(),
            "05_VaultStatsForm.png",
            formSetup: null,
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Estatísticas", window.Title);
            });
    }

    [Fact]
    public void FlaUI_CanCapture_DiagnosticsForm()
    {
        CaptureForm(
            () => new DiagnosticsForm(),
            "06_DiagnosticsForm.png",
            formSetup: null,
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Diagnóstico", window.Title);
            },
            beforeCapture: form =>
            {
                var field = typeof(DiagnosticsForm).GetField("_lstConflicts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field?.GetValue(form) is ListView lst)
                {
                    lst.Items.Clear();
                    lst.Items.Add(new ListViewItem(["Diario-2026-08-28.md", "Notas/Diario-2026-08-28.md", "Hoje 20:30", "1.4 KB"]));
                    lst.Items.Add(new ListViewItem(["Arquitetura.md", "Docs/Arquitetura.md", "Ontem 18:45", "8.2 KB"]));
                }
            });
    }

    [Fact]
    public void FlaUI_CanCapture_ThreeWayDiffViewerForm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Synapse_Diff_Test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        var conflictDir = Path.Combine(tempDir, "_conflitos", "Notas");
        Directory.CreateDirectory(conflictDir);
        var notesDir = Path.Combine(tempDir, "Notas");
        Directory.CreateDirectory(notesDir);

        var localFile = Path.Combine(notesDir, "MinhaIdeia.md");
        var conflictFile = Path.Combine(conflictDir, "MinhaIdeia.conflito-20260828.md");

        File.WriteAllText(localFile, "# Minha Ideia\nAlteração feita localmente no desktop com novas notas de arquitetura.");
        File.WriteAllText(conflictFile, "# Minha Ideia\nAlteração conflitante vinda do GitHub mobile durante a viagem.");

        try
        {
            CaptureForm(
                () => new ThreeWayDiffViewerForm(tempDir, conflictFile),
                "07_ThreeWayDiffViewerForm.png",
                formSetup: null,
                verifyAction: window =>
                {
                    Assert.NotNull(window);
                    Assert.Contains("Resolução", window.Title);
                });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void FlaUI_CanCapture_RemoteConfirmationForm()
    {
        var cmd = new RemoteCommand(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            RemoteCommandType.TypeText,
            new Dictionary<string, string>
            {
                ["processName"] = "Obsidian",
                ["text"] = "[[Arquitetura Pixel Art]] adicionada ao mapa mental."
            },
            "Pixel-Terminal-Remote");

        CaptureForm(
            () => new WinFormsConfirmationPrompt.RemoteConfirmationForm(cmd, TimeSpan.FromSeconds(30), CancellationToken.None),
            "08_RemoteConfirmationForm.png",
            formSetup: null,
            verifyAction: window =>
            {
                Assert.NotNull(window);
                Assert.Contains("Remote", window.Title);
            });
    }

    [Fact]
    public void FlaUI_CanCapture_TrayContextMenu()
    {
        CaptureForm(
            () =>
            {
                var form = new Form
                {
                    Text = "Synapse — Menu da Bandeja (Preview)",
                    Size = new Size(320, 520),
                    StartPosition = FormStartPosition.CenterScreen
                };
                SynapseTheme.ApplyFormChrome(form);

                var toolStrip = new ToolStrip
                {
                    Dock = DockStyle.Fill,
                    LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
                    Renderer = new SynapseMenuRenderer(),
                    BackColor = SynapseTheme.Surface,
                    ForeColor = SynapseTheme.TextPrimary,
                    Font = SynapseTheme.FontBody(8.5f),
                    Padding = new Padding(8, 10, 8, 10),
                    GripStyle = ToolStripGripStyle.Hidden
                };

                var statusHeader = new ToolStripLabel("● STATUS: SINCRONIZADO [OK]")
                {
                    ForeColor = SynapseTheme.NeonGreen,
                    Font = SynapseTheme.FontHeadline(8.5f),
                    Margin = new Padding(6, 4, 6, 6),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                toolStrip.Items.Add(statusHeader);
                toolStrip.Items.Add(new ToolStripSeparator());

                void AddMenuItem(string text)
                {
                    var btn = new ToolStripButton(text)
                    {
                        TextAlign = ContentAlignment.MiddleLeft,
                        Margin = new Padding(4, 2, 4, 2)
                    };
                    toolStrip.Items.Add(btn);
                }

                AddMenuItem("► Captura Rápida (Brain)...");
                AddMenuItem("► Chat com Cofre (RAG)...");
                AddMenuItem("► Flashcards (SM-2)...");
                AddMenuItem("► Estatísticas && Backup...");
                toolStrip.Items.Add(new ToolStripSeparator());
                AddMenuItem("Pausar Sincronização");
                AddMenuItem("Reconectar GitHub");
                AddMenuItem("Controle Remoto: Ativo");
                toolStrip.Items.Add(new ToolStripSeparator());
                AddMenuItem("► Diagnóstico && Conflitos...");
                AddMenuItem("► Configurações...");
                AddMenuItem("► Abrir Pasta de Logs");
                toolStrip.Items.Add(new ToolStripSeparator());
                AddMenuItem("✖ Sair da Bandeja");

                form.Controls.Add(toolStrip);
                return form;
            },
            "09_TrayContextMenu.png",
            formSetup: null,
            verifyAction: window =>
            {
                Assert.NotNull(window);
            });
    }

    private static void CaptureForm(Func<Form> formFactory, string fileName, Action<Form>? formSetup, Action<Window>? verifyAction, Action<Form>? beforeCapture = null)
    {
        Form? form = null;
        var readySignal = new ManualResetEventSlim(false);
        Exception? threadException = null;

        var staThread = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                form = formFactory();
                form.StartPosition = FormStartPosition.CenterScreen;
                formSetup?.Invoke(form);
                form.Show();
                readySignal.Set();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                threadException = ex;
                readySignal.Set();
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();

        try
        {
            var isReady = readySignal.Wait(TimeSpan.FromSeconds(5));
            if (!isReady || threadException != null || form == null)
            {
                throw new InvalidOperationException("Falha ao inicializar o Form na thread STA.", threadException);
            }

            // Garante que a janela renderizou o layout
            Thread.Sleep(300);

            using var automation = new UIA3Automation();
            var element = automation.FromHandle(form.Handle);
            var window = element.AsWindow();

            // Validações no elemento via FlaUI UIA3 na thread de teste
            verifyAction?.Invoke(window);

            // Captura de screenshot de alta fidelidade
            form.Invoke(new Action(() =>
            {
                beforeCapture?.Invoke(form);

                form.Update();
                form.Refresh();

                using var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));

                var targetPath = Path.Combine(OutputDir, fileName);
                bmp.Save(targetPath, ImageFormat.Png);

                var afterDir = Path.Combine(OutputDir, "after");
                Directory.CreateDirectory(afterDir);
                bmp.Save(Path.Combine(afterDir, fileName), ImageFormat.Png);

                if (Directory.Exists(BrainArtifactDir))
                {
                    var brainScreenshots = Path.Combine(BrainArtifactDir, "screenshots");
                    Directory.CreateDirectory(brainScreenshots);
                    bmp.Save(Path.Combine(brainScreenshots, fileName), ImageFormat.Png);
                }
            }));
        }
        finally
        {
            if (form != null && form.IsHandleCreated && !form.IsDisposed)
            {
                try
                {
                    form.Invoke(new Action(() =>
                    {
                        form.Close();
                        form.Dispose();
                    }));
                }
                catch { }
            }
            staThread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
