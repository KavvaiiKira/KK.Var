using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KK.Var.Models;
using KK.Var.ViewModels;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Views
{
    public partial class MainWindow
    {
        private KKProject? _projectPendingDelete;

        private void NewProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_projectDetailsPage is { IsVisible: true } projectDetailsPage &&
                !projectDetailsPage.CanNavigateAway(
                    () => NewProjectButton_OnClick(sender, e)))
            {
                return;
            }

            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (!viewModel.MachineEditor.IsRemoteMachineConfigured)
            {
                ShowSettingsPage(showSettings: true);
                return;
            }

            _showSettingsAfterProjectEditorClose = false;
            viewModel.ClearStatusNotification();
            EnsureCreateProjectPage().BeginCreation();
            ShowProjectEditorPage();
        }

        private async void ImportProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel || viewModel.IsProjectTransferBusy)
            {
                return;
            }
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = viewModel.Localize(Texts.ImportProjectConfiguration),
                    AllowMultiple = false,
                    FileTypeFilter = [FilePickerFileTypes.Json],
                });
                if (files.Count == 0)
                {
                    return;
                }
                await using var stream = await files[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                viewModel.PrepareProjectImport(json);
            }
            catch (Exception exception)
            {
                viewModel.ShowNotification(viewModel.Localize(exception.Message), isError: true);
            }
        }

        private void CancelProjectImportButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.CancelProjectImport();
            }
        }

        private async void ConfirmProjectImportButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel &&
                await viewModel.ConfirmProjectImportAsync() is { } project)
            {
                await viewModel.LoadProjectDetailsAsync(project);
                EnsureProjectDetailsPage().ShowOverviewSection();
                ShowProjectDetailsPage();
            }
        }

        private async void OpenProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var project = GetProject(sender);

            if (project is null || DataContext is not MainViewModel viewModel)
            {
                return;
            }

            viewModel.ClearStatusNotification();
            viewModel.SelectedProject = project;
            await viewModel.LoadProjectDetailsAsync(project);
            EnsureProjectDetailsPage().ShowOverviewSection();
            ShowProjectDetailsPage();
        }

        private async void DeployProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var project = GetProject(sender);

            if (project is null || DataContext is not MainViewModel viewModel)
            {
                return;
            }

            viewModel.ClearStatusNotification();
            viewModel.SelectedProject = project;
            await viewModel.LoadProjectDetailsAsync(project);
            EnsureProjectDetailsPage().ShowDeploySection();
            ShowProjectDetailsPage();
        }

        private void EditProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var project = GetProject(sender);

            if (project is not null)
            {
                BeginProjectEditing(project);
            }
        }

        private void DeleteProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            var project = GetProject(sender);

            if (project is not null)
            {
                ShowDeleteProjectConfirmation(project);
            }
        }

        private void ShowDeleteProjectConfirmation(KKProject project)
        {
            _projectPendingDelete = project;
            DeleteProjectMessage.Text = DataContext is MainViewModel viewModel ?
                viewModel.LocalizeFormat(
                    Texts.ProjectAllLocalArchivesPermanentlyDeleted,
                    project.Name) :
                    string.Format(Texts.ProjectAllLocalArchivesPermanentlyDeleted, project.Name);
            DeleteProjectConfirmation.IsVisible = true;
        }

        private void CancelDeleteProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            _projectPendingDelete = null;
            DeleteProjectConfirmation.IsVisible = false;
        }

        private async void ConfirmDeleteProjectButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_projectPendingDelete is null || DataContext is not MainViewModel viewModel)
            {
                return;
            }

            var project = _projectPendingDelete;
            DeleteProjectConfirmation.IsVisible = false;
            _projectPendingDelete = null;

            if (await viewModel.DeleteProjectAsync(project))
            {
                ShowSettingsPage(showSettings: false);
            }
        }

        private static KKProject? GetProject(object? sender) =>
            (sender as Control)?.DataContext is ProjectTileViewModel tile ?
                tile.Project :
                null;
    }
}
