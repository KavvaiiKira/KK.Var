using System.Linq;
using Avalonia.Threading;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly IDeploymentHistoryService _deploymentHistory;

        private readonly IKKProjectDeploymentService _projectDeploymentService;

        private readonly IDeploymentPreflightService _deploymentPreflightService;

        private readonly IDeploymentOperationQueue _deploymentOperationQueue;

        private readonly ILocalizationService _localizationService;

        public MainViewModel(
            ApplicationSettingsViewModel appSettings,
            ProjectListViewModel projectList,
            RemoteMachineEditorViewModel machineEditor,
            GitHubSourceSelectionViewModel deploymentGitHubSource,
            GitHubAccountViewModel gitHubAccount,
            ProjectEnvironmentEditorViewModel environmentEditor,
            IKKProjectDeploymentService projectDeploymentService,
            IDeploymentHistoryService deploymentHistory,
            DeploymentHistoryViewModel history,
            IDeploymentNotificationService deploymentNotifications,
            ArtifactStorageViewModel artifactStorage,
            IProjectConfigurationTransferService projectConfigurationTransferService,
            IDeploymentPreflightService deploymentPreflightService,
            IDeploymentOperationQueue deploymentOperationQueue,
            DeploymentWorkspaceViewModel deploymentWorkspace,
            ILocalizationService localizationService,
            CreateProjectViewModel projectEditor,
            ProjectDetailsViewModel projectDetails)
        {
            AppSettings = appSettings;
            AppSettings.NotificationRequested += PublishNotification;
            ProjectList = projectList;
            ProjectList.NotificationRequested += PublishNotification;
            ProjectList.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProjectListViewModel.IsBusy))
                {
                    NotifyOperationStateChanged();
                }
            };
            ProjectDetails = projectDetails;
            ProjectDetails.NotificationRequested += PublishNotification;
            ProjectDetails.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ProjectDetailsViewModel.IsProjectDetailsLoading) or
                    nameof(ProjectDetailsViewModel.IsVersionCleanupBusy))
                {
                    NotifyOperationStateChanged();
                    OnPropertyChanged(nameof(CanCheckDeployment));
                }
            };
            MachineEditor = machineEditor;
            MachineEditor.PreflightInvalidated += () => InvalidateDeploymentPreflight();
            MachineEditor.ProfilesReloaded += () =>
            {
                var historyMachineId = SelectedHistoryMachine?.Id;
                SelectedHistoryMachine = MachineEditor.MachineProfiles.FirstOrDefault(machine => machine.Id == historyMachineId);
                projectEditor.SetMachines(MachineEditor.AvailableMachines);
                AppSettings.UpdateFirstRunRequirement();
            };
            MachineEditor.ProfileVerified += AppSettings.MarkProfileVerifiedAsync;
            MachineEditor.PropertyChanged += (_, e) =>
            {
                NotifyOperationStateChanged();
                if (e.PropertyName == nameof(RemoteMachineEditorViewModel.SettingsStatus))
                {
                    HandleSettingsStatusChanged(MachineEditor.SettingsStatus);
                }
                else if (e.PropertyName == nameof(RemoteMachineEditorViewModel.SettingsError))
                {
                    HandleSettingsErrorChanged(MachineEditor.SettingsError);
                }
            };
            GitHubAccount = gitHubAccount;
            GitHubAccount.NotificationRequested += PublishNotification;
            GitHubAccount.PropertyChanged += (_, _) => NotifyOperationStateChanged();
            EnvironmentEditor = environmentEditor;
            EnvironmentEditor.NotificationRequested += PublishNotification;
            EnvironmentEditor.InvalidationRequested += id => InvalidateDeploymentPreflight(id);
            EnvironmentEditor.ReloadRequested += LoadProjectDetailsAsync;
            EnvironmentEditor.PropertyChanged += (_, e) =>
            {
                OnPropertyChanged(nameof(CanCheckDeployment));
                OnPropertyChanged(nameof(CanExportProjectConfiguration));
                NotifyOperationStateChanged();
            };
            _projectDeploymentService = projectDeploymentService;
            _deploymentHistory = deploymentHistory;
            History = history;
            History.NotificationRequested += PublishNotification;
            History.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeploymentHistoryViewModel.IsLoading))
                {
                    NotifyOperationStateChanged();
                }
            };
            deploymentNotifications.DeploymentCompleted += notice =>
                Dispatcher.UIThread.Post(() => DeploymentCompleted?.Invoke(notice));
            ArtifactStorage = artifactStorage;
            ArtifactStorage.ErrorReported += error => MachineEditor.SettingsError = error;
            ArtifactStorage.StatusReported += status => MachineEditor.SettingsStatus = status;
            ArtifactStorage.RootChanged += root => AppSettings.Settings.ArtifactsDirectoryPath = root;
            ArtifactStorage.PropertyChanged += (_, _) => NotifyOperationStateChanged();
            _projectConfigurationTransferService = projectConfigurationTransferService;
            _deploymentPreflightService = deploymentPreflightService;
            _deploymentOperationQueue = deploymentOperationQueue;
            DeploymentWorkspace = deploymentWorkspace;
            DeploymentWorkspace.StateChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanCheckDeployment));
                NotifyOperationStateChanged();
            };
            _localizationService = localizationService;
            DeploymentGitHubSource = deploymentGitHubSource;
            DeploymentGitHubSource.PropertyChanged += DeploymentGitHubSource_OnPropertyChanged;
            ProjectEditor = projectEditor;
            ProjectEditor.ProjectCreated += ProjectEditor_OnProjectCreated;
            ProjectEditor.ProjectUpdated += ProjectEditor_OnProjectUpdated;
            ProjectEditor.PropertyChanged += ProjectEditor_OnPropertyChanged;
            MachineEditor.RefreshAuthenticationMethods();
            RefreshLocalizedState();
        }

        public ApplicationSettingsViewModel AppSettings { get; }

        public ProjectListViewModel ProjectList { get; }

        public ProjectDetailsViewModel ProjectDetails { get; }

        public ProjectEnvironmentEditorViewModel EnvironmentEditor { get; }

        public GitHubAccountViewModel GitHubAccount { get; }

        public ArtifactStorageViewModel ArtifactStorage { get; }

        public DeploymentHistoryViewModel History { get; }

        public RemoteMachineEditorViewModel MachineEditor { get; }

        public DeploymentWorkspaceViewModel DeploymentWorkspace { get; }

        public CreateProjectViewModel ProjectEditor { get; }

        public bool IsOperationRunning =>
            MachineEditor.IsConnectionCheckRunning ||
            GitHubAccount.IsGitHubAuthorizationPending ||
            ProjectList.IsBusy ||
            ProjectDetails.IsProjectDetailsLoading ||
            History.IsLoading ||
            IsPreflightChecking ||
            IsProjectTransferBusy ||
            ArtifactStorage.IsArtifactStorageBusy ||
            EnvironmentEditor.IsEnvironmentSaving ||
            DeploymentWorkspace.IsDeploymentRunning ||
            ProjectDetails.IsVersionCleanupBusy ||
            ProjectEditor.IsSaving ||
            ProjectEditor.IsLoadingRepositories;

        public string OperationStatusText
        {
            get
            {
                if (MachineEditor.IsConnectionCheckRunning)
                {
                    return Texts.CheckingSSHConnection;
                }

                if (GitHubAccount.IsGitHubAuthorizationPending)
                {
                    return Texts.ConnectingGitHub;
                }

                if (ProjectEditor.IsLoadingRepositories)
                {
                    return Texts.LoadingGitHubRepositories;
                }

                if (ProjectList.IsBusy)
                {
                    return Texts.DeletingProject;
                }

                if (ProjectDetails.IsProjectDetailsLoading)
                {
                    return Texts.LoadingProject;
                }

                if (History.IsLoading)
                {
                    return Texts.LoadingHistory;
                }

                if (IsPreflightChecking)
                {
                    return Texts.DeployPreflightChecks;
                }

                if (IsProjectTransferBusy)
                {
                    return Texts.TransferringProjectConfiguration;
                }

                if (ArtifactStorage.IsArtifactStorageBusy)
                {
                    return Texts.PreparingMovingLocalArchives;
                }

                if (EnvironmentEditor.IsEnvironmentSaving)
                {
                    return Texts.SavingEnvironmentVariables;
                }

                if (DeploymentWorkspace.IsDeploymentRunning)
                {
                    return string.IsNullOrWhiteSpace(DeploymentWorkspace.DeploymentProgressMessage) ?
                        Texts.RunningDeploy :
                        DeploymentWorkspace.DeploymentProgressMessage;
                }

                if (ProjectDetails.IsVersionCleanupBusy)
                {
                    return Texts.CleaningUpLocalVersions;
                }

                return ProjectEditor.IsSaving ?
                    Texts.SavingProject :
                    Texts.NoActiveOperations;
            }
        }

        private void NotifyOperationStateChanged()
        {
            OnPropertyChanged(nameof(IsOperationRunning));
            OnPropertyChanged(nameof(OperationStatusText));
        }
    }
}
