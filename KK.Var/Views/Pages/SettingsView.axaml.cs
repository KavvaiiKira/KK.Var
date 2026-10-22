using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages
{
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
                if (!viewModel.MachineEditor.IsMachineProfileLoading && !viewModel.MachineEditor.IsConnectionCheckRunning)
                {
                    viewModel.MachineEditor.InvalidatePendingMachineVerification();
                }
            }
        }

        private async void CopyGitHubCodeButton_OnClick(
            object? sender,
            RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel ||
                string.IsNullOrWhiteSpace(viewModel.GitHubAccount.GitHubUserCode))
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(viewModel.GitHubAccount.GitHubUserCode);
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
                await viewModel.ArtifactStorage.PrepareArtifactStorageMigrationAsync(
                    folders[0].Path.LocalPath);
            }
        }

        private async void ResetArtifactsDirectoryButton_OnClick(
            object? sender,
            RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.ArtifactStorage.PrepareArtifactStorageMigrationAsync(null);
            }
        }

        private async void ApplyArtifactMigrationButton_OnClick(
            object? sender,
            RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.ArtifactStorage.ApplyArtifactStorageMigrationAsync();
            }
        }

        private void CancelArtifactMigrationButton_OnClick(
            object? sender,
            RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ArtifactStorage.CancelArtifactStorageMigration();
            }
        }
    }
}
