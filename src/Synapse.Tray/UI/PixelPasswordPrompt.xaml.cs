using System.Windows;

namespace Synapse.Tray.UI;

/// <summary>Pede uma senha em estetica pixel art, substituindo o dialogo improvisado do WinForms.</summary>
public partial class PixelPasswordPrompt : PixelWindow
{
    private string _result = string.Empty;

    public PixelPasswordPrompt()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>Retorna a senha digitada, ou string vazia se o usuario cancelou.</summary>
    public static string Ask(string message, Window? owner = null)
    {
        var prompt = new PixelPasswordPrompt();
        prompt.PromptText.Text = message;

        if (owner is not null)
        {
            prompt.Owner = owner;
            prompt.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        prompt.ShowDialog();
        return prompt._result;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        _result = PasswordInput.Password.Trim();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _result = string.Empty;
        Close();
    }
}
