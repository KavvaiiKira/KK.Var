using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(TextBox.TextChangedEvent, SettingsInput_OnChanged);
    }

    private void SettingsInput_OnChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.InvalidateDeploymentPreflight();
        }
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

    private async void SelectArtifactsDirectoryButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = viewModel.Localize("Выберите каталог локальных версий"),
                AllowMultiple = false,
            });

        if (folders.Count > 0 && folders[0].Path.IsFile)
        {
            await viewModel.PrepareArtifactStorageMigrationAsync(
                folders[0].Path.LocalPath);
        }
    }

    private async void ResetArtifactsDirectoryButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.PrepareArtifactStorageMigrationAsync(null);
        }
    }

    private async void ApplyArtifactMigrationButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.ApplyArtifactStorageMigrationAsync();
        }
    }

    private void CancelArtifactMigrationButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelArtifactStorageMigration();
        }
    }
}
