using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Launcher.App.Services;

/// <summary>Small reusable ContentDialog flows — confirm/prompt — used across instance, file and world actions.</summary>
public static class DialogHelpers
{
    public static async Task<bool> ConfirmAsync(
        IContentDialogService service, string title, string message, string primaryText = "Удалить")
    {
        var dialog = new ContentDialog
        {
            Style = (Style)Application.Current.FindResource(typeof(ContentDialog)),
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Отмена",
        };

        var result = await service.ShowAsync(dialog, CancellationToken.None);
        return result == ContentDialogResult.Primary;
    }

    /// <summary>In-app text editor for config/text files — a big monospace TextBox in a dialog, so
    /// players never get bounced out to Notepad. Returns the edited text, or null on cancel.</summary>
    public static async Task<string?> EditTextAsync(
        IContentDialogService service, string title, string content)
    {
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = content,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 13,
            MinWidth = 640,
            Height = 420,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Style = (Style)Application.Current.FindResource(typeof(ContentDialog)),
            Title = title,
            Content = textBox,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DialogMaxWidth = 760,
            DialogMaxHeight = 600,
        };

        var result = await service.ShowAsync(dialog, CancellationToken.None);
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    public static async Task<string?> PromptAsync(
        IContentDialogService service, string title, string initialValue, string primaryText = "Сохранить")
    {
        var textBox = new System.Windows.Controls.TextBox { Text = initialValue, Margin = new Thickness(0, 8, 0, 0) };
        var dialog = new ContentDialog
        {
            Style = (Style)Application.Current.FindResource(typeof(ContentDialog)),
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Отмена",
        };

        var result = await service.ShowAsync(dialog, CancellationToken.None);
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }
}
