using Avalonia.Controls;
using KK.Var.Services;
using KK.Var.Services.Implementations;
using KK.Var.Views.Design;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KK.Var.Views
{
    public partial class MainWindow : Window
    {
        private readonly ILocalizationService _localizationService;

        private readonly ILogger<WindowsDeploymentNotifications> _notificationLogger;

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
            CreateProjectPage.CloseRequested += CreateProjectPage_OnCloseRequested;
            ProjectDetailsPage.BackRequested += ProjectDetailsPage_OnBackRequested;
            ProjectDetailsPage.EditRequested += ProjectDetailsPage_OnEditRequested;
            ProjectDetailsPage.DeleteRequested += ProjectDetailsPage_OnDeleteRequested;
            ProjectDetailsPage.DeployRequested += ProjectDetailsPage_OnDeployRequested;
            ProjectDetailsPage.DeploymentSelected += ProjectDetailsPage_OnDeploymentSelected;
            HistoryPage.DeploymentSelected += HistoryPage_OnDeploymentSelected;
            DeploymentDetailsPage.BackRequested += DeploymentDetailsPage_OnBackRequested;
            DeploymentDetailsPage.RepeatDeployRequested += DeploymentDetailsPage_OnRepeatDeployRequested;

            PropertyChanged += (_, args) =>
            {
                if (args.Property == WindowStateProperty)
                {
                    UpdateWindowChrome();
                }
            };

            UpdateWindowChrome();
        }
    }
}
