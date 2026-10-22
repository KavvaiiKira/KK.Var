using System;
using Avalonia.Controls;
using KK.Var.ViewModels;

namespace KK.Var.Views
{
    public partial class MainWindow
    {
        private bool _firstRunHandled;

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!CanCloseApplication())
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        private bool CanCloseApplication()
        {
            if (DataContext is not MainViewModel { DeploymentWorkspace.IsDeploymentRunning: true } viewModel)
            {
                return true;
            }

            viewModel.ShowNotification(
                viewModel.Localize(
                                "Дождитесь завершения Deploy. Закрытие приложения во время переключения версии заблокировано."),
                isError: true);
            return false;
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            if (_firstRunHandled || DataContext is not MainViewModel viewModel)
            {
                return;
            }

            _firstRunHandled = true;
            InitializeDeploymentNotifications(viewModel);
            await viewModel.LoadSettingsAsync();
            UpdateProjectsAccessState(viewModel);

            if (viewModel.AppSettings.Settings.IsFirstRunCompleted)
            {
                return;
            }

            var firstRunWindow = new FirstRunWindow(_localizationService);

            ModalBackdrop.IsVisible = true;

            try
            {
                _ = await firstRunWindow.ShowDialog<bool>(this);
            }
            finally
            {
                ModalBackdrop.IsVisible = false;
            }

            viewModel.AppSettings.RequireFirstRunSetup();
            ShowSettingsPage(showSettings: true);
        }
    }
}
