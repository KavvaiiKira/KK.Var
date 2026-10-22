using Avalonia.Controls;
using KK.Var.Services;
using KK.Var.Services.Implementations;
using KK.Var.Views.Design;
using KK.Var.Views.Pages;
using KK.Var.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KK.Var.Views
{
    public partial class MainWindow : Window
    {
        private readonly ILocalizationService _localizationService;

        private readonly ILogger<WindowsDeploymentNotifications> _notificationLogger;

        private CreateProjectView? _createProjectPage;

        private ProjectDetailsView? _projectDetailsPage;

        private HistoryView? _historyPage;

        private DeploymentDetailsView? _deploymentDetailsPage;

        private SettingsView? _settingsPage;

        public MainWindow()
            : this(DesignServices.Localization, NullLogger<WindowsDeploymentNotifications>.Instance)
        {
        }

        public MainWindow(
            ILocalizationService localizationService,
            ILogger<WindowsDeploymentNotifications> notificationLogger)
        {
            _localizationService = localizationService;
            _notificationLogger = notificationLogger;

            InitializeComponent();

            PropertyChanged += (_, args) =>
            {
                if (args.Property == WindowStateProperty)
                {
                    UpdateWindowChrome();
                }
            };

            UpdateWindowChrome();
        }

        private CreateProjectView EnsureCreateProjectPage()
        {
            if (_createProjectPage is not null)
            {
                return _createProjectPage;
            }

            var page = new CreateProjectView
            {
                DataContext = (DataContext as MainViewModel)?.ProjectEditor,
            };
            page.CloseRequested += CreateProjectPage_OnCloseRequested;
            _createProjectPage = page;
            return page;
        }

        private ProjectDetailsView EnsureProjectDetailsPage()
        {
            if (_projectDetailsPage is not null)
            {
                return _projectDetailsPage;
            }

            var page = new ProjectDetailsView
            {
                DataContext = DataContext,
            };
            page.BackRequested += ProjectDetailsPage_OnBackRequested;
            page.EditRequested += ProjectDetailsPage_OnEditRequested;
            page.DeleteRequested += ProjectDetailsPage_OnDeleteRequested;
            page.DeployRequested += ProjectDetailsPage_OnDeployRequested;
            page.DeploymentSelected += ProjectDetailsPage_OnDeploymentSelected;
            _projectDetailsPage = page;
            return page;
        }

        private HistoryView EnsureHistoryPage()
        {
            if (_historyPage is not null)
            {
                return _historyPage;
            }

            var page = new HistoryView
            {
                DataContext = DataContext,
            };
            page.DeploymentSelected += HistoryPage_OnDeploymentSelected;
            _historyPage = page;
            return page;
        }

        private DeploymentDetailsView EnsureDeploymentDetailsPage()
        {
            if (_deploymentDetailsPage is not null)
            {
                return _deploymentDetailsPage;
            }

            var page = new DeploymentDetailsView();
            page.BackRequested += DeploymentDetailsPage_OnBackRequested;
            page.RepeatDeployRequested += DeploymentDetailsPage_OnRepeatDeployRequested;
            _deploymentDetailsPage = page;
            return page;
        }

        private SettingsView EnsureSettingsPage()
        {
            if (_settingsPage is not null)
            {
                return _settingsPage;
            }

            _settingsPage = new SettingsView
            {
                DataContext = DataContext,
            };
            return _settingsPage;
        }

        private void ShowSecondaryPage(Control page)
        {
            if (SecondaryPageHost.Content is Control currentPage && currentPage != page)
            {
                currentPage.IsVisible = false;
            }

            SecondaryPageHost.Content = page;
            page.IsVisible = true;
            SecondaryPageHost.IsVisible = true;
        }

        private void HideSecondaryPage()
        {
            if (SecondaryPageHost.Content is Control currentPage)
            {
                currentPage.IsVisible = false;
            }

            SecondaryPageHost.IsVisible = false;
            SecondaryPageHost.Content = null;
        }
    }
}
