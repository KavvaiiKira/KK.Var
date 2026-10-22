using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using KK.Var.Models;
using KK.Var.Services.Implementations;
using KK.Var.ViewModels;

namespace KK.Var.Views
{
    public partial class MainWindow
    {
        private WindowsDeploymentNotifications? _windowsNotifications;

        private MainViewModel? _notificationViewModel;

        private DeploymentCompletionNotice? _pendingNotificationNavigation;

        private void InitializeDeploymentNotifications(MainViewModel viewModel)
        {
            _notificationViewModel = viewModel;
            _windowsNotifications = new WindowsDeploymentNotifications(
                TryGetPlatformHandle()?.Handle ?? 0,
                notice => Dispatcher.UIThread.Post(() => NavigateNotification(notice)),
                _notificationLogger);
            viewModel.DeploymentCompleted += DeploymentCompleted;
            viewModel.AppSettings.PropertyChanged += NotificationSettingsChanged;
        }

        private void NotificationSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ApplicationSettingsViewModel.DeploymentNotificationsEnabled) &&
                _notificationViewModel?.AppSettings.DeploymentNotificationsEnabled == false)
            {
                _windowsNotifications?.Clear();
            }
        }

        private void DeploymentCompleted(DeploymentCompletionNotice notice)
        {
            if (_notificationViewModel is not { AppSettings.DeploymentNotificationsEnabled: true } viewModel)
            {
                return;
            }
            var isCurrentDetails = DeploymentDetailsPage.IsVisible && notice.DeploymentId is { } id &&
                viewModel.SelectedDeploymentDetails?.Deployment.Id == id;
            var isCurrentDeploy = ProjectDetailsPage.IsVisible && ProjectDetailsPage.IsDeploySectionVisible &&
                viewModel.SelectedProject?.Id == notice.ProjectId;
            if (IsActive && WindowState != WindowState.Minimized && (isCurrentDetails || isCurrentDeploy))
            {
                return;
            }
            _windowsNotifications?.Show(
                notice,
                "KK.Var - " + viewModel.NotificationTitle(notice),
                string.IsNullOrWhiteSpace(notice.ProjectName) ?
                    viewModel.Localize("Откройте подробности операции.") :
                    notice.ProjectName);
        }

        private async void NavigateNotification(DeploymentCompletionNotice notice)
        {
            try
            {
                Show();
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Activate();
                if (ProjectDetailsPage.IsVisible &&
                    !ProjectDetailsPage.CanNavigateAway(() => NavigateNotification(notice)))
                {
                    return;
                }
                if (CreateProjectPage.IsVisible)
                {
                    _pendingNotificationNavigation = notice;
                    CreateProjectPage.RequestClose();
                    return;
                }
                await ShowNotificationTargetAsync(notice);
            }
            catch (Exception exception)
            {
                _notificationViewModel?.ShowNotification(_localizationService.Get(exception.Message), isError: true);
            }
        }

        private async Task ShowNotificationTargetAsync(DeploymentCompletionNotice notice)
        {
            if (_notificationViewModel is not { } viewModel || !await viewModel.OpenNotificationTargetAsync(notice))
            {
                return;
            }
            _showSettingsAfterProjectEditorClose = false;
            _showHistoryAfterProjectEditorClose = false;
            _deploymentDetailsReturnToProject = true;
            if (notice.DeploymentId is not null)
            {
                ShowDeploymentDetailsPage();
            }
            else
            {
                ProjectDetailsPage.ShowDeploySection(prepare: false);
                ShowProjectDetailsPage();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_notificationViewModel is { } viewModel)
            {
                viewModel.DeploymentCompleted -= DeploymentCompleted;
                viewModel.AppSettings.PropertyChanged -= NotificationSettingsChanged;
            }
            _windowsNotifications?.Dispose();
            base.OnClosed(e);
        }
    }
}
