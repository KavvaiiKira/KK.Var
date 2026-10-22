using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel : ViewModelBase
    {
        private const string DefaultToolchainPath = "toolchains/linux-{architecture}.cmake";

        private const string DefaultCustomBuildArguments = "{output}\r\n{runtime}";

        private readonly IBuildConfigurationService _buildConfigurationService;

        private readonly IKKProjectService _projectService;

        private readonly IGitHubService _gitHubService;

        private readonly IGitHubAuthenticationService _gitHubAuthenticationService;

        private readonly ILocalizationService _localizationService;

        private readonly IRemoteMachineService _remoteMachineService;

        private bool _isResetting;

        private Guid? _editingProjectId;

        private KKProject? _editingProject;

        public CreateProjectViewModel(
            IBuildConfigurationService buildConfigurationService,
            IKKProjectService projectService,
            IGitHubService gitHubService,
            IGitHubAuthenticationService gitHubAuthenticationService,
            IRemoteMachineService remoteMachineService,
            ILocalizationService localizationService)
        {
            _buildConfigurationService = buildConfigurationService;
            _projectService = projectService;
            _gitHubService = gitHubService;
            _gitHubAuthenticationService = gitHubAuthenticationService;
            _localizationService = localizationService;
            HealthCheckEditor = new ProjectHealthCheckEditorViewModel(localizationService);
            HealthCheckEditor.Changed += (_, _) => MarkDirty();
            HealthCheckEditor.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
            BuildEditor = new ProjectBuildEditorViewModel(buildConfigurationService, localizationService);
            BuildEditor.Changed += (_, _) => MarkDirty();
            BuildEditor.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
            _remoteMachineService = remoteMachineService;
            GitHubBranchSelection = new GitHubSourceSelectionViewModel(
                gitHubService,
                gitHubAuthenticationService,
                localizationService);
            GitHubBranchSelection.BranchChanged += (_, _) => MarkDirty();
            RefreshLocalization();
        }

        public event EventHandler<KKProject>? ProjectCreated;

        public event EventHandler<KKProject>? ProjectUpdated;

        public ObservableCollection<EnumOption> SourceTypes { get; } = [];

        public ObservableCollection<EnumOption> BuildProviders { get; } = [];

        public ObservableCollection<EnumOption> HealthCheckTypes { get; } = [];

        public ObservableCollection<GitHubRepository> GitHubRepositories { get; } = [];

        public GitHubSourceSelectionViewModel GitHubBranchSelection { get; }

        [ObservableProperty]
        public partial string Name { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string Description { get; set; } = string.Empty;

        [ObservableProperty]
        public partial ProjectSourceType SelectedSourceType { get; set; } = ProjectSourceType.LocalDirectory;

        [ObservableProperty]
        public partial string LocalDirectoryPath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial GitHubRepository? SelectedGitHubRepository { get; set; }

        [ObservableProperty]
        public partial ProjectBuildProvider SelectedBuildProvider { get; set; } = ProjectBuildProvider.Unknown;

        [ObservableProperty]
        public partial string RemoteServiceName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string RemoteExecutableFileName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string RemoteDeploymentDirectory { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ProjectEnvironmentFilePath { get; set; } = string.Empty;

        public string BuildConfigurationJson
        {
            get => BuildEditor.BuildConfigurationJson;
            set => BuildEditor.BuildConfigurationJson = value;
        }

        [ObservableProperty]
        public partial int MaxStoredVersions { get; set; } = 10;

        [ObservableProperty]
        public partial string MaxStoredVersionSizeMb { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool HasUnsavedChanges { get; private set; }

        [ObservableProperty]
        public partial bool IsSaving { get; private set; }

        [ObservableProperty]
        public partial bool IsLoadingRepositories { get; private set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; private set; } = string.Empty;

        public bool IsLocalSource => SelectedSourceType == ProjectSourceType.LocalDirectory;

        public bool IsGitHubSource => SelectedSourceType == ProjectSourceType.GitHubRepository;

        public bool IsEditing => _editingProjectId.HasValue;

        public string EditorTitle => IsEditing ?
            Texts.EditProject :
            Texts.NewProject2;

        public string SaveButtonText => IsEditing ?
            Texts.SaveChanges :
            Texts.SaveProject;

        public void Reset()
        {
            _isResetting = true;
            BuildEditor.IsResetting = true;
            _editingProjectId = null;
            _editingProject = null;

            Name = string.Empty;
            Description = string.Empty;
            SelectedSourceType = ProjectSourceType.LocalDirectory;
            LocalDirectoryPath = string.Empty;
            SelectedGitHubRepository = null;
            SelectedProjectMachine = ProjectMachines.FirstOrDefault();
            MachineSwitchConfirmed = false;
            GitHubBranchSelection.Configure(null, null);
            SelectedBuildProvider = ProjectBuildProvider.Unknown;
            RemoteServiceName = string.Empty;
            RemoteExecutableFileName = string.Empty;
            RemoteDeploymentDirectory = string.Empty;
            ProjectEnvironmentFilePath = string.Empty;
            BuildConfigurationJson = "{}";
            LoadBuildFields();
            SelectedHealthCheckType = ProjectHealthCheckType.None;
            HealthCheckTimeoutSeconds = 30;
            HealthCheckIntervalSeconds = 5;
            HealthCheckAttempts = 3;
            HealthCheckStabilityDelaySeconds = 10;
            HealthCheckHttpUrl = string.Empty;
            HealthCheckTcpPort = 8080;
            HealthCheckCommand = string.Empty;
            MaxStoredVersions = 10;
            MaxStoredVersionSizeMb = string.Empty;
            ErrorMessage = string.Empty;
            HasUnsavedChanges = false;

            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(SaveButtonText));

            _isResetting = false;
            BuildEditor.IsResetting = false;
        }

        public void LoadProject(KKProject project)
        {
            ArgumentNullException.ThrowIfNull(project);

            _isResetting = true;
            BuildEditor.IsResetting = true;
            _editingProjectId = project.Id;
            _editingProject = project;
            SelectedProjectMachine = ProjectMachines.FirstOrDefault(machine => machine.Id == project.RemoteMachineId);
            MachineSwitchConfirmed = false;

            Name = project.Name;
            Description = project.Description ?? string.Empty;
            SelectedSourceType = project.SourceType;

            LocalDirectoryPath = project.LocalDirectoryPath ?? string.Empty;

            SelectedGitHubRepository =
                project.SourceType == ProjectSourceType.GitHubRepository &&
                project.GitHubRepositoryId.HasValue &&
                project.GitHubRepositoryFullName is not null &&
                project.GitHubCloneUrl is not null ?
                    new GitHubRepository(
                        project.GitHubRepositoryId.Value,
                        project.GitHubRepositoryFullName.Split('/')[^1],
                        project.GitHubRepositoryFullName,
                        project.GitHubCloneUrl,
                        string.Empty,
                        false) :
                    null;

            SelectedBuildProvider = project.BuildProvider;
            GitHubBranchSelection.Configure(project.GitHubRepositoryFullName, project.GitHubBranch);
            RemoteServiceName = project.RemoteServiceName;
            RemoteExecutableFileName = project.RemoteExecutableFileName;
            RemoteDeploymentDirectory = project.RemoteDeploymentDirectory;
            ProjectEnvironmentFilePath = project.ProjectEnvironmentFilePath;
            BuildConfigurationJson = project.BuildConfigurationJson;
            LoadBuildFields();
            SelectedHealthCheckType = project.HealthCheckType;
            HealthCheckTimeoutSeconds = project.HealthCheckTimeoutSeconds ?? 30;
            HealthCheckIntervalSeconds = project.HealthCheckIntervalSeconds ?? 5;
            HealthCheckAttempts = project.HealthCheckAttempts ?? 3;
            HealthCheckStabilityDelaySeconds =
                project.HealthCheckStabilityDelaySeconds ?? 10;
            HealthCheckHttpUrl = project.HealthCheckHttpUrl ?? string.Empty;
            HealthCheckTcpPort = project.HealthCheckTcpPort ?? 8080;
            HealthCheckCommand = project.HealthCheckCommand ?? string.Empty;
            MaxStoredVersions = project.MaxStoredVersions;
            MaxStoredVersionSizeMb = project.MaxStoredVersionBytes.HasValue ?
                (project.MaxStoredVersionBytes.Value / 1_048_576d).ToString(
                    "0.##",
                    CultureInfo.CurrentCulture) :
                string.Empty;
            ErrorMessage = string.Empty;
            HasUnsavedChanges = false;

            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(SaveButtonText));

            _isResetting = false;
            BuildEditor.IsResetting = false;
        }

        public async Task<bool> SaveAsync()
        {
            if (IsSaving)
            {
                return false;
            }

            UpdateBuildConfiguration();
            ErrorMessage = Validate();

            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                return false;
            }

            var project = new KKProject
            {
                Id = _editingProjectId ?? Guid.NewGuid(),
                RemoteMachineId = SelectedProjectMachine!.Id,
                Name = Name,
                Description = Description,
                SourceType = IsLocalSource ?
                    ProjectSourceType.LocalDirectory :
                    ProjectSourceType.GitHubRepository,
                LocalDirectoryPath = IsLocalSource ? LocalDirectoryPath : null,
                GitHubRepositoryId = IsGitHubSource ? SelectedGitHubRepository?.Id : null,
                GitHubRepositoryFullName = IsGitHubSource ?
                    SelectedGitHubRepository?.FullName :
                    null,
                GitHubCloneUrl = IsGitHubSource ? SelectedGitHubRepository?.CloneUrl : null,
                GitHubBranch = IsGitHubSource ? GitHubBranchSelection.BranchName : null,
                BuildProvider = SelectedBuildProvider,
                BuildConfigurationJson = BuildConfigurationJson,
                RemoteServiceName = RemoteServiceName,
                RemoteExecutableFileName = RemoteExecutableFileName,
                RemoteDeploymentDirectory = RemoteDeploymentDirectory,
                ProjectEnvironmentFilePath = ProjectEnvironmentFilePath,
                EnvironmentFileFormat = _editingProject?.EnvironmentFileFormat ?? EnvironmentFileFormat.Json,
                HealthCheckType = SelectedHealthCheckType,
                HealthCheckTimeoutSeconds = HasHealthCheckRetrySettings ?
                    HealthCheckTimeoutSeconds :
                    null,
                HealthCheckIntervalSeconds = HasHealthCheckRetrySettings ?
                    HealthCheckIntervalSeconds :
                    null,
                HealthCheckAttempts = HasHealthCheckRetrySettings ?
                    HealthCheckAttempts :
                    null,
                HealthCheckStabilityDelaySeconds = IsStabilityDelayHealthCheck ?
                    HealthCheckStabilityDelaySeconds :
                    null,
                HealthCheckHttpUrl = IsHttpHealthCheck ? HealthCheckHttpUrl : null,
                HealthCheckTcpPort = IsTcpHealthCheck ? HealthCheckTcpPort : null,
                HealthCheckCommand = IsCommandHealthCheck ? HealthCheckCommand : null,
                MaxStoredVersions = MaxStoredVersions,
                MaxStoredVersionBytes = ParseStorageLimitBytes(),
                EnvironmentVariables = _editingProject?.EnvironmentVariables ?? [],
                Versions = _editingProject?.Versions ?? [],
                Deployments = _editingProject?.Deployments ?? [],
            };

            IsSaving = true;

            try
            {
                if (IsEditing)
                {
                    await _projectService.UpdateAsync(project);

                    Reset();
                    ProjectUpdated?.Invoke(this, project);
                }
                else
                {
                    var createdProject = await _projectService.CreateAsync(project);

                    Reset();
                    ProjectCreated?.Invoke(this, createdProject);
                }

                return true;
            }
            catch (Exception exception)
            {
                ErrorMessage = Localize(exception.Message);
                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        [RelayCommand]
        private async Task RefreshGitHubRepositoriesAsync()
        {
            if (IsLoadingRepositories)
            {
                return;
            }

            IsLoadingRepositories = true;
            ErrorMessage = string.Empty;

            try
            {
                var token = await _gitHubAuthenticationService.GetTokenAsync();
                if (token is null)
                {
                    ErrorMessage = Texts.ConnectGitHubSettingsPageFirst;
                    return;
                }

                var repositories = await _gitHubService.GetRepositoriesAsync(token.AccessToken);

                var selectedRepositoryId = SelectedGitHubRepository?.Id;
                var selectedBranch = GitHubBranchSelection.BranchName;
                _isResetting = true;
                BuildEditor.IsResetting = true;

                GitHubRepositories.Clear();

                foreach (var repository in repositories)
                {
                    GitHubRepositories.Add(repository);
                }

                if (selectedRepositoryId.HasValue)
                {
                    _isResetting = true;
                    BuildEditor.IsResetting = true;
                    SelectedGitHubRepository = GitHubRepositories.FirstOrDefault(
                        repository => repository.Id == selectedRepositoryId.Value);
                    GitHubBranchSelection.Configure(SelectedGitHubRepository?.FullName, selectedBranch);
                    _isResetting = false;
                    BuildEditor.IsResetting = false;
                }
            }
            catch (Exception exception)
            {
                ErrorMessage = Localize(exception.Message);
            }
            finally
            {
                IsLoadingRepositories = false;
                _isResetting = false;
                BuildEditor.IsResetting = false;
            }
        }

        partial void OnNameChanged(string value) => MarkDirty();

        partial void OnDescriptionChanged(string value) => MarkDirty();

        partial void OnSelectedSourceTypeChanged(ProjectSourceType value)
        {
            MarkDirty();
            OnPropertyChanged(nameof(IsLocalSource));
            OnPropertyChanged(nameof(IsGitHubSource));

            if (value == ProjectSourceType.GitHubRepository && GitHubRepositories.Count == 0)
            {
                _ = RefreshGitHubRepositoriesAsync();
            }
        }

        partial void OnLocalDirectoryPathChanged(string value) => MarkDirty();

        partial void OnSelectedGitHubRepositoryChanged(GitHubRepository? value)
        {
            if (value?.FullName != GitHubBranchSelection.Repository)
            {
                GitHubBranchSelection.Configure(value?.FullName, null);
            }
            MarkDirty();
        }

        partial void OnSelectedBuildProviderChanged(ProjectBuildProvider value)
        {
            NotifyBuildFields();
            if (_isResetting)
            {
                return;
            }
            if (IsCppBuild && string.IsNullOrWhiteSpace(ToolchainFile))
            {
                ToolchainFile = DefaultToolchainPath;
            }
            if (IsCustomBuild && string.IsNullOrWhiteSpace(BuildArgumentsText))
            {
                BuildArgumentsText = DefaultCustomBuildArguments;
            }
            UpdateBuildConfiguration();
            MarkDirty();
        }

        partial void OnRemoteServiceNameChanged(string value) => MarkDirty();

        partial void OnRemoteExecutableFileNameChanged(string value) => MarkDirty();

        partial void OnRemoteDeploymentDirectoryChanged(string value) => MarkDirty();

        partial void OnProjectEnvironmentFilePathChanged(string value) => MarkDirty();

        partial void OnMaxStoredVersionsChanged(int value) => MarkDirty();

        partial void OnMaxStoredVersionSizeMbChanged(string value) => MarkDirty();

        private void MarkDirty()
        {
            if (!_isResetting)
            {
                HasUnsavedChanges = true;
                ErrorMessage = string.Empty;
            }
        }
    }
}
