using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public partial class CreateProjectViewModel : ViewModelBase
{
    public const string LocalSource = "Локальная папка";
    public const string GitHubSource = "Репозиторий GitHub";
    public const string AutomaticBuild = "Определить автоматически";
    public const string NoHealthCheck = "Без проверки";
    public const string StabilityDelayHealthCheck = "Задержка стабильности";
    public const string HttpHealthCheck = "HTTP-запрос";
    public const string TcpHealthCheck = "TCP-порт";
    public const string CommandHealthCheck = "Команда";

    private readonly IKKProjectService? _projectService;
    private readonly IGitHubService? _gitHubService;
    private readonly IGitHubAuthenticationService? _gitHubAuthenticationService;
    private readonly ILocalizationService? _localizationService;
    private bool _isResetting;
    private Guid? _editingProjectId;
    private KKProject? _editingProject;

    public CreateProjectViewModel()
    {
        RefreshLocalization();
    }

    public CreateProjectViewModel(
        IKKProjectService projectService,
        IGitHubService gitHubService,
        IGitHubAuthenticationService gitHubAuthenticationService,
        ILocalizationService localizationService)
    {
        _projectService = projectService;
        _gitHubService = gitHubService;
        _gitHubAuthenticationService = gitHubAuthenticationService;
        _localizationService = localizationService;
        RefreshLocalization();
    }

    public event EventHandler<KKProject>? ProjectCreated;

    public event EventHandler<KKProject>? ProjectUpdated;

    public ObservableCollection<string> SourceTypes { get; } = [];

    public ObservableCollection<string> BuildProviders { get; } = [];

    public ObservableCollection<string> HealthCheckTypes { get; } = [];

    public ObservableCollection<GitHubRepository> GitHubRepositories { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedSourceType { get; set; } = LocalSource;

    [ObservableProperty]
    public partial string LocalDirectoryPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GitHubRepository? SelectedGitHubRepository { get; set; }

    [ObservableProperty]
    public partial string SelectedBuildProvider { get; set; } = AutomaticBuild;

    [ObservableProperty]
    public partial string RemoteServiceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RemoteExecutableFileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RemoteDeploymentDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProjectEnvironmentFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BuildConfigurationJson { get; set; } = "{}";

    [ObservableProperty]
    public partial string SelectedHealthCheckType { get; set; } = NoHealthCheck;

    [ObservableProperty]
    public partial int HealthCheckTimeoutSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial int HealthCheckIntervalSeconds { get; set; } = 5;

    [ObservableProperty]
    public partial int HealthCheckAttempts { get; set; } = 3;

    [ObservableProperty]
    public partial int HealthCheckStabilityDelaySeconds { get; set; } = 10;

    [ObservableProperty]
    public partial string HealthCheckHttpUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int HealthCheckTcpPort { get; set; } = 8080;

    [ObservableProperty]
    public partial string HealthCheckCommand { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingRepositories { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool IsLocalSource => Canonicalize(SelectedSourceType) == LocalSource;

    public bool IsGitHubSource => Canonicalize(SelectedSourceType) == GitHubSource;

    public bool IsHealthCheckEnabled =>
        MapHealthCheckType() != ProjectHealthCheckType.None;

    public bool IsStabilityDelayHealthCheck =>
        MapHealthCheckType() == ProjectHealthCheckType.StabilityDelay;

    public bool IsHttpHealthCheck =>
        MapHealthCheckType() == ProjectHealthCheckType.Http;

    public bool IsTcpHealthCheck =>
        MapHealthCheckType() == ProjectHealthCheckType.Tcp;

    public bool IsCommandHealthCheck =>
        MapHealthCheckType() == ProjectHealthCheckType.Command;

    public bool HasHealthCheckRetrySettings =>
        MapHealthCheckType() is ProjectHealthCheckType.Http or
            ProjectHealthCheckType.Tcp or
            ProjectHealthCheckType.Command;

    public bool IsEditing => _editingProjectId.HasValue;

    public string EditorTitle => IsEditing ?
        Localize("Редактирование проекта") :
        Localize("Новый проект");

    public string SaveButtonText => IsEditing ?
        Localize("Сохранить изменения") :
        Localize("Сохранить проект");

    public void Reset()
    {
        _isResetting = true;
        _editingProjectId = null;
        _editingProject = null;

        Name = string.Empty;
        Description = string.Empty;
        SelectedSourceType = Localize(LocalSource);
        LocalDirectoryPath = string.Empty;
        SelectedGitHubRepository = null;
        SelectedBuildProvider = Localize(AutomaticBuild);
        RemoteServiceName = string.Empty;
        RemoteExecutableFileName = string.Empty;
        RemoteDeploymentDirectory = string.Empty;
        ProjectEnvironmentFilePath = string.Empty;
        BuildConfigurationJson = "{}";
        SelectedHealthCheckType = Localize(NoHealthCheck);
        HealthCheckTimeoutSeconds = 30;
        HealthCheckIntervalSeconds = 5;
        HealthCheckAttempts = 3;
        HealthCheckStabilityDelaySeconds = 10;
        HealthCheckHttpUrl = string.Empty;
        HealthCheckTcpPort = 8080;
        HealthCheckCommand = string.Empty;
        ErrorMessage = string.Empty;
        HasUnsavedChanges = false;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveButtonText));

        _isResetting = false;
    }

    public void LoadProject(KKProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        _isResetting = true;
        _editingProjectId = project.Id;
        _editingProject = project;

        Name = project.Name;
        Description = project.Description ?? string.Empty;
        SelectedSourceType = Localize(
            project.SourceType == ProjectSourceType.GitHubRepository ?
                GitHubSource :
                LocalSource);

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

        SelectedBuildProvider = MapBuildProvider(project.BuildProvider);
        RemoteServiceName = project.RemoteServiceName;
        RemoteExecutableFileName = project.RemoteExecutableFileName;
        RemoteDeploymentDirectory = project.RemoteDeploymentDirectory;
        ProjectEnvironmentFilePath = project.ProjectEnvironmentFilePath;
        BuildConfigurationJson = project.BuildConfigurationJson;
        SelectedHealthCheckType = MapHealthCheckType(project.HealthCheckType);
        HealthCheckTimeoutSeconds = project.HealthCheckTimeoutSeconds ?? 30;
        HealthCheckIntervalSeconds = project.HealthCheckIntervalSeconds ?? 5;
        HealthCheckAttempts = project.HealthCheckAttempts ?? 3;
        HealthCheckStabilityDelaySeconds =
            project.HealthCheckStabilityDelaySeconds ?? 10;
        HealthCheckHttpUrl = project.HealthCheckHttpUrl ?? string.Empty;
        HealthCheckTcpPort = project.HealthCheckTcpPort ?? 8080;
        HealthCheckCommand = project.HealthCheckCommand ?? string.Empty;
        ErrorMessage = string.Empty;
        HasUnsavedChanges = false;

        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveButtonText));

        _isResetting = false;
    }

    public async Task<bool> SaveAsync()
    {
        if (_projectService is null || IsSaving)
        {
            return false;
        }

        ErrorMessage = Validate();

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            return false;
        }

        var project = new KKProject
        {
            Id = _editingProjectId ?? Guid.NewGuid(),
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
            BuildProvider = MapBuildProvider(),
            BuildConfigurationJson = BuildConfigurationJson,
            RemoteServiceName = RemoteServiceName,
            RemoteExecutableFileName = RemoteExecutableFileName,
            RemoteDeploymentDirectory = RemoteDeploymentDirectory,
            ProjectEnvironmentFilePath = ProjectEnvironmentFilePath,
            EnvironmentFileFormat = _editingProject?.EnvironmentFileFormat ?? EnvironmentFileFormat.Json,
            HealthCheckType = MapHealthCheckType(),
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
            ErrorMessage = exception.Message;
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
        if (_gitHubService is null ||
            _gitHubAuthenticationService is null ||
            IsLoadingRepositories)
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
                ErrorMessage = Localize("Сначала подключите GitHub на странице настроек.");
                return;
            }

            var selectedRepositoryId = SelectedGitHubRepository?.Id;
            var repositories = await _gitHubService.GetRepositoriesAsync(token.AccessToken);

            GitHubRepositories.Clear();

            foreach (var repository in repositories)
            {
                GitHubRepositories.Add(repository);
            }

            if (selectedRepositoryId.HasValue)
            {
                _isResetting = true;
                SelectedGitHubRepository = GitHubRepositories.FirstOrDefault(
                    repository => repository.Id == selectedRepositoryId.Value);
                _isResetting = false;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsLoadingRepositories = false;
        }
    }

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnDescriptionChanged(string value) => MarkDirty();

    partial void OnSelectedSourceTypeChanged(string value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(IsLocalSource));
        OnPropertyChanged(nameof(IsGitHubSource));

        if (Canonicalize(value) == GitHubSource && GitHubRepositories.Count == 0)
        {
            _ = RefreshGitHubRepositoriesAsync();
        }
    }

    partial void OnLocalDirectoryPathChanged(string value) => MarkDirty();

    partial void OnSelectedGitHubRepositoryChanged(GitHubRepository? value) => MarkDirty();

    partial void OnSelectedBuildProviderChanged(string value)
    {
        if (_isResetting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(BuildConfigurationJson) ||
            BuildConfigurationJson.Trim() == "{}")
        {
            BuildConfigurationJson = MapBuildProvider() switch
            {
                ProjectBuildProvider.DotNet =>
                    "{\r\n  \"configuration\": \"Release\",\r\n  \"workingDirectory\": \".\",\r\n  \"buildArguments\": [],\r\n  \"environment\": {}\r\n}",
                ProjectBuildProvider.Go or ProjectBuildProvider.Python =>
                    "{\r\n  \"workingDirectory\": \".\",\r\n  \"buildArguments\": [],\r\n  \"environment\": {}\r\n}",
                ProjectBuildProvider.Cpp =>
                    "{\r\n  \"configuration\": \"Release\",\r\n  \"workingDirectory\": \".\",\r\n  \"toolchainFile\": \"toolchains/linux-{architecture}.cmake\",\r\n  \"cmakeGenerator\": \"Ninja\",\r\n  \"configureArguments\": [],\r\n  \"buildArguments\": [],\r\n  \"environment\": {}\r\n}",
                ProjectBuildProvider.Custom =>
                    "{\r\n  \"command\": \"\",\r\n  \"workingDirectory\": \".\",\r\n  \"buildArguments\": [\"{output}\", \"{runtime}\"],\r\n  \"environment\": {}\r\n}",
                _ => "{}",
            };
        }

        MarkDirty();
    }

    partial void OnRemoteServiceNameChanged(string value) => MarkDirty();

    partial void OnRemoteExecutableFileNameChanged(string value) => MarkDirty();

    partial void OnRemoteDeploymentDirectoryChanged(string value) => MarkDirty();

    partial void OnBuildConfigurationJsonChanged(string value) => MarkDirty();

    partial void OnProjectEnvironmentFilePathChanged(string value) => MarkDirty();

    partial void OnSelectedHealthCheckTypeChanged(string value)
    {
        MarkDirty();
        OnPropertyChanged(nameof(IsHealthCheckEnabled));
        OnPropertyChanged(nameof(IsStabilityDelayHealthCheck));
        OnPropertyChanged(nameof(IsHttpHealthCheck));
        OnPropertyChanged(nameof(IsTcpHealthCheck));
        OnPropertyChanged(nameof(IsCommandHealthCheck));
        OnPropertyChanged(nameof(HasHealthCheckRetrySettings));
    }

    partial void OnHealthCheckTimeoutSecondsChanged(int value) => MarkDirty();

    partial void OnHealthCheckIntervalSecondsChanged(int value) => MarkDirty();

    partial void OnHealthCheckAttemptsChanged(int value) => MarkDirty();

    partial void OnHealthCheckStabilityDelaySecondsChanged(int value) => MarkDirty();

    partial void OnHealthCheckHttpUrlChanged(string value) => MarkDirty();

    partial void OnHealthCheckTcpPortChanged(int value) => MarkDirty();

    partial void OnHealthCheckCommandChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (!_isResetting)
        {
            HasUnsavedChanges = true;
            ErrorMessage = string.Empty;
        }
    }

    private string Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return Localize("Укажите название проекта.");
        }

        if (IsLocalSource && string.IsNullOrWhiteSpace(LocalDirectoryPath))
        {
            return Localize("Выберите локальную папку проекта.");
        }

        if (IsGitHubSource && SelectedGitHubRepository is null)
        {
            return Localize("Выберите репозиторий GitHub.");
        }

        if (string.IsNullOrWhiteSpace(RemoteServiceName))
        {
            return Localize("Укажите название systemd-сервиса.");
        }

        if (string.IsNullOrWhiteSpace(RemoteExecutableFileName))
        {
            return Localize("Укажите исполняемый файл или точку входа.");
        }

        if (string.IsNullOrWhiteSpace(RemoteDeploymentDirectory))
        {
            return Localize(
                "Укажите директорию развёртывания на удалённой машине.");
        }

        if (string.IsNullOrWhiteSpace(ProjectEnvironmentFilePath))
        {
            return Localize(
                "Укажите путь к файлу переменных окружения внутри проекта.");
        }

        var healthCheckError = ValidateHealthCheck();
        if (!string.IsNullOrEmpty(healthCheckError))
        {
            return healthCheckError;
        }

        try
        {
            var configurationJson =
                string.IsNullOrWhiteSpace(BuildConfigurationJson) ?
                    "{}" :
                    BuildConfigurationJson;

            using var document = System.Text.Json.JsonDocument.Parse(configurationJson);

            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return Localize("Параметры сборки должны быть JSON-объектом.");
            }

            var configuration = System.Text.Json.JsonSerializer
                .Deserialize<ProjectBuildConfiguration>(
                    configurationJson,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    }) ?? new ProjectBuildConfiguration();

            var provider = MapBuildProvider();
            if (provider == ProjectBuildProvider.Custom &&
                string.IsNullOrWhiteSpace(configuration.Command))
            {
                return Localize("Укажите command для пользовательской сборки.");
            }

            if (provider == ProjectBuildProvider.Cpp &&
                string.IsNullOrWhiteSpace(configuration.ToolchainFile))
            {
                return Localize("Укажите toolchainFile для сборки C++ под Linux.");
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return Localize("Параметры сборки содержат некорректный JSON.");
        }

        return string.Empty;
    }

    private string ValidateHealthCheck()
    {
        if (IsStabilityDelayHealthCheck &&
            HealthCheckStabilityDelaySeconds is < 1 or > 3600)
        {
            return Localize(
                "Задержка стабильности должна быть от 1 до 3600 секунд.");
        }

        if (!HasHealthCheckRetrySettings)
        {
            return string.Empty;
        }

        if (HealthCheckTimeoutSeconds is < 1 or > 3600)
        {
            return Localize(
                "Timeout health-check должен быть от 1 до 3600 секунд.");
        }

        if (HealthCheckIntervalSeconds is < 1 or > 3600)
        {
            return Localize(
                "Интервал health-check должен быть от 1 до 3600 секунд.");
        }

        if (HealthCheckAttempts is < 1 or > 100)
        {
            return Localize(
                "Количество попыток health-check должно быть от 1 до 100.");
        }

        if (IsHttpHealthCheck &&
            (!Uri.TryCreate(HealthCheckHttpUrl.Trim(), UriKind.Absolute, out var uri) ||
                (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))))
        {
            return Localize(
                "Укажите абсолютный HTTP- или HTTPS-адрес health-check.");
        }

        if (IsTcpHealthCheck && HealthCheckTcpPort is < 1 or > 65535)
        {
            return Localize("TCP-порт должен быть от 1 до 65535.");
        }

        if (IsCommandHealthCheck && string.IsNullOrWhiteSpace(HealthCheckCommand))
        {
            return Localize("Укажите команду health-check.");
        }

        return string.Empty;
    }

    private ProjectBuildProvider MapBuildProvider() => SelectedBuildProvider switch
    {
        var value when Canonicalize(value) == ".NET" => ProjectBuildProvider.DotNet,
        var value when Canonicalize(value) == "Python" => ProjectBuildProvider.Python,
        var value when Canonicalize(value) == "Go" => ProjectBuildProvider.Go,
        var value when Canonicalize(value) == "C++" => ProjectBuildProvider.Cpp,
        var value when Canonicalize(value) == "Свой сценарий" => ProjectBuildProvider.Custom,
        _ => ProjectBuildProvider.Unknown,
    };

    private string MapBuildProvider(ProjectBuildProvider provider) => provider switch
    {
        ProjectBuildProvider.DotNet => ".NET",
        ProjectBuildProvider.Python => "Python",
        ProjectBuildProvider.Go => "Go",
        ProjectBuildProvider.Cpp => "C++",
        ProjectBuildProvider.Custom => Localize("Свой сценарий"),
        _ => Localize(AutomaticBuild),
    };

    private ProjectHealthCheckType MapHealthCheckType() =>
        Canonicalize(SelectedHealthCheckType) switch
        {
            StabilityDelayHealthCheck => ProjectHealthCheckType.StabilityDelay,
            HttpHealthCheck => ProjectHealthCheckType.Http,
            TcpHealthCheck => ProjectHealthCheckType.Tcp,
            CommandHealthCheck => ProjectHealthCheckType.Command,
            _ => ProjectHealthCheckType.None,
        };

    private string MapHealthCheckType(ProjectHealthCheckType type) => type switch
    {
        ProjectHealthCheckType.StabilityDelay => Localize(StabilityDelayHealthCheck),
        ProjectHealthCheckType.Http => Localize(HttpHealthCheck),
        ProjectHealthCheckType.Tcp => Localize(TcpHealthCheck),
        ProjectHealthCheckType.Command => Localize(CommandHealthCheck),
        _ => Localize(NoHealthCheck),
    };

    public void RefreshLocalization()
    {
        var sourceKey = Canonicalize(SelectedSourceType);
        var providerKey = Canonicalize(SelectedBuildProvider);
        var healthCheckKey = Canonicalize(SelectedHealthCheckType);

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            sourceKey = LocalSource;
        }

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            providerKey = AutomaticBuild;
        }

        if (string.IsNullOrWhiteSpace(healthCheckKey))
        {
            healthCheckKey = NoHealthCheck;
        }

        _isResetting = true;

        SourceTypes.Clear();
        SourceTypes.Add(Localize(LocalSource));
        SourceTypes.Add(Localize(GitHubSource));
        BuildProviders.Clear();
        BuildProviders.Add(Localize(AutomaticBuild));
        BuildProviders.Add(".NET");
        BuildProviders.Add("Python");
        BuildProviders.Add("Go");
        BuildProviders.Add("C++");
        BuildProviders.Add(Localize("Свой сценарий"));
        HealthCheckTypes.Clear();
        HealthCheckTypes.Add(Localize(NoHealthCheck));
        HealthCheckTypes.Add(Localize(StabilityDelayHealthCheck));
        HealthCheckTypes.Add(Localize(HttpHealthCheck));
        HealthCheckTypes.Add(Localize(TcpHealthCheck));
        HealthCheckTypes.Add(Localize(CommandHealthCheck));
        SelectedSourceType = Localize(sourceKey);
        SelectedBuildProvider = Localize(providerKey);
        SelectedHealthCheckType = Localize(healthCheckKey);

        _isResetting = false;

        OnPropertyChanged(nameof(IsLocalSource));
        OnPropertyChanged(nameof(IsGitHubSource));
        OnPropertyChanged(nameof(IsHealthCheckEnabled));
        OnPropertyChanged(nameof(IsStabilityDelayHealthCheck));
        OnPropertyChanged(nameof(IsHttpHealthCheck));
        OnPropertyChanged(nameof(IsTcpHealthCheck));
        OnPropertyChanged(nameof(IsCommandHealthCheck));
        OnPropertyChanged(nameof(HasHealthCheckRetrySettings));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private string Localize(string key) => _localizationService?.Get(key) ?? key;

    private string Canonicalize(string value) => _localizationService?.GetKey(value) ?? value;
}
