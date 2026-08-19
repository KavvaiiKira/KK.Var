using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void CopyGitHubCodeButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            string.IsNullOrWhiteSpace(viewModel.GitHubUserCode))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(viewModel.GitHubUserCode);
        }
    }
}
