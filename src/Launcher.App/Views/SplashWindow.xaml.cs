using System.Windows;

namespace Launcher.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Updates the status line under the spinner (marshals to the UI thread).</summary>
    public void SetStatus(string text) =>
        Dispatcher.Invoke(() => StatusText.Text = text);

    /// <summary>Switches the bottom bar to determinate mode and sets 0–100% progress.</summary>
    public void SetProgress(int percent) =>
        Dispatcher.Invoke(() =>
        {
            Progress.IsIndeterminate = false;
            Progress.Value = Math.Clamp(percent, 0, 100);
        });
}
