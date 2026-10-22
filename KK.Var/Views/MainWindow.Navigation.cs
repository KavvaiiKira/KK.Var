using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KK.Var.Constants;
using KK.Var.Models;
using KK.Var.ViewModels;

namespace KK.Var.Views
{
    public partial class MainWindow
    {
        private bool _showSettingsAfterProjectEditorClose;

        private bool _showHistoryAfterProjectEditorClose;

        private bool _deploymentDetailsReturnToProject;

        private void ProjectsNavigationButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_projectDetailsPage is { IsVisible: true } projectDetailsPage &&
                !projectDetailsPage.CanNavigateAway(
                    () => ProjectsNavigationButton_OnClick(sender, e)))
            {
                return;
            }

            if (_createProjectPage is { IsVisible: true } createProjectPage)
            {
                _showSettingsAfterProjectEditorClose = false;
                _showHistoryAfterProjectEditorClose = false;
                createProjectPage.RequestClose();
                return;
            }

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
            }

            ShowSettingsPage(showSettings: false);
        }

        private void SettingsNavigationButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_projectDetailsPage is { IsVisible: true } projectDetailsPage &&
                !projectDetailsPage.CanNavigateAway(
                    () => SettingsNavigationButton_OnClick(sender, e)))
            {
                return;
            }

            if (_createProjectPage is { IsVisible: true } createProjectPage)
            {
                _showSettingsAfterProjectEditorClose = true;
                _showHistoryAfterProjectEditorClose = false;
                createProjectPage.RequestClose();
                return;
            }

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
            }

            ShowSettingsPage(showSettings: true);
        }

        private async void HistoryNavigationButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_projectDetailsPage is { IsVisible: true } projectDetailsPage &&
                !projectDetailsPage.CanNavigateAway(
                    () => HistoryNavigationButton_OnClick(sender, e)))
            {
                return;
            }

            if (_createProjectPage is { IsVisible: true } createProjectPage)
            {
                _showSettingsAfterProjectEditorClose = false;
                _showHistoryAfterProjectEditorClose = true;
                createProjectPage.RequestClose();
                return;
            }

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
                await viewModel.LoadHistoryAsync();
            }

            ShowHistoryPage();
        }

        private async void CreateProjectPage_OnCloseRequested(object? sender, EventArgs e)
        {
            HideSecondaryPage();
            if (_pendingNotificationNavigation is { } notice)
            {
                _pendingNotificationNavigation = null;
                (DataContext as MainViewModel)?.TakeCreatedProjectForNavigation();
                await ShowNotificationTargetAsync(notice);
                return;
            }

            if (DataContext is MainViewModel projectViewModel &&
                projectViewModel.TakeCreatedProjectForNavigation() is { } createdProject)
            {
                projectViewModel.SelectedProject = createdProject;
                await projectViewModel.LoadProjectDetailsAsync(createdProject);
                EnsureProjectDetailsPage().ShowOverviewSection();
                ShowProjectDetailsPage();
                _showHistoryAfterProjectEditorClose = false;
                _showSettingsAfterProjectEditorClose = false;
                return;
            }

            if (_showHistoryAfterProjectEditorClose && DataContext is MainViewModel historyViewModel)
            {
                historyViewModel.ClearStatusNotification();
                await historyViewModel.LoadHistoryAsync();
                ShowHistoryPage();
                _showHistoryAfterProjectEditorClose = false;
                _showSettingsAfterProjectEditorClose = false;
                return;
            }

            if (_showSettingsAfterProjectEditorClose && DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
            }

            ShowSettingsPage(_showSettingsAfterProjectEditorClose);
            _showSettingsAfterProjectEditorClose = false;
            _showHistoryAfterProjectEditorClose = false;
        }

        private void ProjectDetailsPage_OnBackRequested(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
            }

            ShowSettingsPage(showSettings: false);
        }

        private void ProjectDetailsPage_OnEditRequested(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel
                {
                    SelectedProject:
                    {
                    } project
                })
            {
                BeginProjectEditing(project);
            }
        }

        private void ProjectDetailsPage_OnDeleteRequested(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel
                {
                    SelectedProject:
                    {
                    } project
                })
            {
                ShowDeleteProjectConfirmation(project);
            }
        }

        private void ProjectDetailsPage_OnDeployRequested(object? sender, EventArgs e) =>
            EnsureProjectDetailsPage().ShowDeploySection();

        private async void ProjectDetailsPage_OnDeploymentSelected(
            DeploymentHistoryItemViewModel item) =>
            await OpenDeploymentDetailsAsync(item, returnToProject: true);

        private async void HistoryPage_OnDeploymentSelected(DeploymentHistoryItemViewModel item) =>
            await OpenDeploymentDetailsAsync(item, returnToProject: false);

        private async Task OpenDeploymentDetailsAsync(
            DeploymentHistoryItemViewModel item,
            bool returnToProject)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            viewModel.ClearStatusNotification();
            await viewModel.LoadDeploymentDetailsAsync(item);
            _deploymentDetailsReturnToProject = returnToProject;
            ShowDeploymentDetailsPage();
        }

        private void DeploymentDetailsPage_OnBackRequested(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
                viewModel.ClearDeploymentDetails();
            }

            if (_deploymentDetailsReturnToProject)
            {
                EnsureProjectDetailsPage().ShowHistorySection();
                ShowProjectDetailsPage();
                return;
            }

            ShowHistoryPage();
        }

        private async void DeploymentDetailsPage_OnRepeatDeployRequested(
            object? sender,
            EventArgs e)
        {
            if (DataContext is not MainViewModel
                {
                    SelectedDeploymentDetails:
                    {
                    } details
                } viewModel)
            {
                return;
            }

            var deployment = details.Deployment;
            var project = viewModel.ProjectList.Projects.FirstOrDefault(
                item => item.Id == deployment.KKProjectId) ?? deployment.Project;

            viewModel.ClearStatusNotification();
            viewModel.SelectedProject = project;
            await viewModel.LoadProjectDetailsAsync(project);
            viewModel.PrepareRepeatDeployment(deployment);
            viewModel.ClearDeploymentDetails();
            EnsureProjectDetailsPage().ShowDeploySection();
            ShowProjectDetailsPage();
        }

        private void BeginProjectEditing(KKProject project)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
            }

            _showSettingsAfterProjectEditorClose = false;
            _showHistoryAfterProjectEditorClose = false;
            EnsureCreateProjectPage().BeginEditing(project);
            ShowProjectEditorPage();
        }

        private void ShowProjectEditorPage()
        {
            ProjectsPage.IsVisible = false;
            var page = EnsureCreateProjectPage();
            page.IsVisible = true;
            ShowSecondaryPage(page);
            SetNavigationState(ProjectsNavigationButton, isActive: true);
            SetNavigationState(HistoryNavigationButton, isActive: false);
            SetNavigationState(SettingsNavigationButton, isActive: false);
        }

        private void ShowProjectDetailsPage()
        {
            ProjectsPage.IsVisible = false;
            var page = EnsureProjectDetailsPage();
            page.IsVisible = true;
            ShowSecondaryPage(page);
            SetNavigationState(ProjectsNavigationButton, isActive: true);
            SetNavigationState(HistoryNavigationButton, isActive: false);
            SetNavigationState(SettingsNavigationButton, isActive: false);
        }

        private void ShowHistoryPage()
        {
            ProjectsPage.IsVisible = false;
            var page = EnsureHistoryPage();
            page.IsVisible = true;
            ShowSecondaryPage(page);
            SetNavigationState(ProjectsNavigationButton, isActive: false);
            SetNavigationState(HistoryNavigationButton, isActive: true);
            SetNavigationState(SettingsNavigationButton, isActive: false);
        }

        private void ShowDeploymentDetailsPage()
        {
            ProjectsPage.IsVisible = false;
            var page = EnsureDeploymentDetailsPage();
            page.DataContext = (DataContext as MainViewModel)?.SelectedDeploymentDetails;
            page.IsVisible = true;
            ShowSecondaryPage(page);
            SetNavigationState(ProjectsNavigationButton, isActive: _deploymentDetailsReturnToProject);
            SetNavigationState(HistoryNavigationButton, isActive: !_deploymentDetailsReturnToProject);
            SetNavigationState(SettingsNavigationButton, isActive: false);
        }

        private void OpenRequiredSettingsButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ClearStatusNotification();
            }

            ShowSettingsPage(showSettings: true);
        }

        private async void LanguageButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                await viewModel.SwitchLanguageAsync();
            }
        }

        private void HelpButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.OpenReadme();
            }
        }

        private void ShowSettingsPage(bool showSettings)
        {
            if (DataContext is MainViewModel viewModel)
            {
                if (!showSettings && _settingsPage is { IsVisible: true })
                {
                    viewModel.ClearSettingsStatus();
                }

                UpdateProjectsAccessState(viewModel);
                if (showSettings)
                {
                    _ = viewModel.ArtifactStorage.RefreshArtifactStorageSummaryAsync();
                }
            }

            ProjectsPage.IsVisible = !showSettings;
            if (showSettings)
            {
                var page = EnsureSettingsPage();
                page.IsVisible = true;
                ShowSecondaryPage(page);
            }
            else
            {
                HideSecondaryPage();
            }
            SetNavigationState(ProjectsNavigationButton, !showSettings);
            SetNavigationState(HistoryNavigationButton, false);
            SetNavigationState(SettingsNavigationButton, showSettings);
        }

        private void UpdateProjectsAccessState(MainViewModel viewModel)
        {
            var isBlocked = !viewModel.MachineEditor.IsRemoteMachineConfigured;

            ProjectsAccessBackdrop.IsVisible = isBlocked;
            ProjectsHeader.Opacity = isBlocked ? 0.28 : 1;
            ProjectsList.Opacity = isBlocked ? 0.28 : 1;
            ProjectsHeader.IsHitTestVisible = !isBlocked;
            ProjectsList.IsHitTestVisible = !isBlocked;
        }

        private static void SetNavigationState(Button button, bool isActive)
        {
            if (isActive && !button.Classes.Contains(UiConstants.ActiveNavigationClass))
            {
                button.Classes.Add(UiConstants.ActiveNavigationClass);
            }
            else if (!isActive)
            {
                button.Classes.Remove(UiConstants.ActiveNavigationClass);
            }
        }
    }
}
