using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string PrivateKeyAuthentication = "SSH-ключ";
    private const string PasswordAuthentication = "Пароль";
    private const int HistoryPageSize = 50;
    private const string JsonEnvironmentFormat = "JSON";
    private const string DotEnvEnvironmentFormat = ".env";
    private const string ShellEnvironmentFormat = "Shell (export)";
    private const string YamlEnvironmentFormat = "YAML";

    private readonly IUserSettingsService? _userSettingsService;
    private readonly IRemoteConnectionService? _remoteConnectionService;
    private readonly IGitHubService? _gitHubService;
    private readonly IGitHubTokenStore? _gitHubTokenStore;
    private readonly IKKProjectService? _projectService;
    private readonly IKKProjectEnvironmentService? _projectEnvironmentService;
    private readonly IKKProjectVersionService? _projectVersionService;
    private readonly IKKProjectDeploymentService? _projectDeploymentService;
    private readonly ILocalizationService? _localizationService;
    private CancellationTokenSource? _gitHubAuthorizationCancellation;
    private CancellationTokenSource? _historySearchCancellation;
    private CancellationTokenSource? _environmentAutoSaveCancellation;
    private bool _isLoadingEnvironment;
    private int _environmentChangeVersion;
    private KKProject? _createdProjectForNavigation;
    private Guid? _deploymentEditorProjectId;

    public MainViewModel()
    {
        ProjectEditor = new CreateProjectViewModel();
        RefreshAuthenticationMethods();
    }

    public MainViewModel(
        IUserSettingsService userSettingsService,
        IRemoteConnectionService remoteConnectionService,
        IGitHubService gitHubService,
        IGitHubTokenStore gitHubTokenStore,
        IKKProjectService projectService,
        IKKProjectEnvironmentService projectEnvironmentService,
        IKKProjectVersionService projectVersionService,
        IKKProjectDeploymentService projectDeploymentService,
        ILocalizationService localizationService,
        CreateProjectViewModel projectEditor)
    {
        _userSettingsService = userSettingsService;
        _remoteConnectionService = remoteConnectionService;
        _gitHubService = gitHubService;
        _gitHubTokenStore = gitHubTokenStore;
        _projectService = projectService;
        _projectEnvironmentService = projectEnvironmentService;
        _projectVersionService = projectVersionService;
        _projectDeploymentService = projectDeploymentService;
        _localizationService = localizationService;
        ProjectEditor = projectEditor;
        ProjectEditor.ProjectCreated += ProjectEditor_OnProjectCreated;
        ProjectEditor.ProjectUpdated += ProjectEditor_OnProjectUpdated;
        ProjectEditor.PropertyChanged += ProjectEditor_OnPropertyChanged;
        RefreshAuthenticationMethods();
        RefreshLocalizedState();
    }

    public ObservableCollection<KKProject> Projects { get; } = [];

    public ObservableCollection<ProjectTileViewModel> ProjectTiles { get; } = [];

    public ObservableCollection<EnvironmentVariableRowViewModel> EnvironmentVariables { get; }
        = [];

    public ObservableCollection<ProjectVersionItemViewModel> ProjectVersions { get; } = [];

    public ObservableCollection<DeploymentHistoryItemViewModel> ProjectHistory { get; } = [];

    public ObservableCollection<DeploymentHistoryItemViewModel> HistoryItems { get; } = [];

    public ObservableCollection<string> HistoryProjectFilters { get; } = [];

    public IReadOnlyList<string> EnvironmentFileFormats { get; } =
        [JsonEnvironmentFormat, DotEnvEnvironmentFormat, ShellEnvironmentFormat, YamlEnvironmentFormat];

    public CreateProjectViewModel ProjectEditor { get; }

    [ObservableProperty]
    public partial UserSettings Settings { get; set; } = new();

    [ObservableProperty]
    public partial string SettingsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SettingsError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NotificationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNotificationError { get; set; }

    [ObservableProperty]
    public partial KKProject? SelectedProject { get; set; }

    [ObservableProperty]
    public partial bool IsProjectOperationRunning { get; set; }

    [ObservableProperty]
    public partial bool IsProjectDetailsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEnvironmentSaving { get; set; }

    [ObservableProperty]
    public partial bool IsDeploymentRunning { get; set; }

    [ObservableProperty]
    public partial int DeploymentProgressPercentage { get; set; }

    [ObservableProperty]
    public partial string DeploymentProgressMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeploymentVersionTag { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeploymentDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeploymentLogText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HistorySearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedHistoryProject { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedEnvironmentFileFormat { get; set; } =
        JsonEnvironmentFormat;

    [ObservableProperty]
    public partial bool HasUnsavedEnvironmentChanges { get; set; }

    [ObservableProperty]
    public partial string EnvironmentSaveStatus { get; set; } = "Все изменения сохранены";

    [ObservableProperty]
    public partial bool HasMoreHistoryItems { get; set; }

    [ObservableProperty]
    public partial bool HasMoreProjectHistoryItems { get; set; }

    [ObservableProperty]
    public partial bool IsFirstRunSetupRequired { get; set; }

    [ObservableProperty]
    public partial bool IsConnectionCheckRunning { get; set; }

    [ObservableProperty]
    public partial string RemoteMachineArchitecture { get; set; } =
        "Будет определена автоматически при проверке подключения";

    [ObservableProperty]
    public partial bool IsGitHubConnected { get; set; }

    [ObservableProperty]
    public partial bool IsGitHubAuthorizationPending { get; set; }

    [ObservableProperty]
    public partial string GitHubAccountDisplay { get; set; } = "Не подключён";

    [ObservableProperty]
    public partial string GitHubConnectionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GitHubUserCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GitHubVerificationUrl { get; set; } =
        "https://github.com/login/device";

    public ObservableCollection<string> AuthenticationMethods { get; } = [];

    public string LanguageButtonText =>
        _localizationService?.CurrentLanguage == ApplicationLanguage.English
            ? "EN"
            : "RU";

    public string LanguageButtonToolTip =>
        _localizationService?.CurrentLanguage == ApplicationLanguage.English
            ? Localize("Переключить на русский")
            : Localize("Переключить на английский");

    [ObservableProperty]
    public partial string AuthenticationMethod { get; set; } =
        PrivateKeyAuthentication;

    public bool IsPrivateKeyAuthentication =>
        Canonicalize(AuthenticationMethod) == PrivateKeyAuthentication;

    public bool IsPasswordAuthentication =>
        Canonicalize(AuthenticationMethod) == PasswordAuthentication;

    public bool IsRemoteMachineConfigured =>
        Settings.IsFirstRunCompleted &&
        string.IsNullOrEmpty(ValidateRemoteMachineSettings());

    public bool HasNotification => !string.IsNullOrWhiteSpace(NotificationMessage);

    public bool HasErrorNotification => HasNotification && IsNotificationError;

    public bool HasSuccessNotification => HasNotification && !IsNotificationError;

    public bool HasNoHistoryItems => HistoryItems.Count == 0;

    public bool HasNoProjectVersions => ProjectVersions.Count == 0;

    public bool HasNoProjectHistory => ProjectHistory.Count == 0;

    public string SelectedProjectSourceDisplay => SelectedProject is null
        ? string.Empty
        : SelectedProject.SourceType == ProjectSourceType.GitHubRepository
            ? $"GitHub · {SelectedProject.GitHubRepositoryFullName}"
            : $"{Localize("Локальная папка")} · {SelectedProject.LocalDirectoryPath}";

    public string SelectedProjectLastDeploymentDisplay
    {
        get
        {
            var deployment = SelectedProject?.Deployments.MaxBy(item => item.StartedAtUtc);
            return deployment is null
                ? Localize("Не выполнялся")
                : deployment.StartedAtUtc.ToLocalTime().ToString("g");
        }
    }

    public string SelectedProjectLatestVersionTag =>
        SelectedProject?.Versions.MaxBy(version => version.CreatedAtUtc)?.Tag
        ?? Localize("Нет версий");

    public bool IsOperationRunning =>
        IsConnectionCheckRunning ||
        IsGitHubAuthorizationPending ||
        IsProjectOperationRunning ||
        IsProjectDetailsLoading ||
        IsEnvironmentSaving ||
        IsDeploymentRunning ||
        ProjectEditor.IsSaving ||
        ProjectEditor.IsLoadingRepositories;

    public string OperationStatusText
    {
        get
        {
            if (IsConnectionCheckRunning)
            {
                return Localize("Проверка SSH-подключения");
            }

            if (IsGitHubAuthorizationPending)
            {
                return Localize("Подключение GitHub");
            }

            if (ProjectEditor.IsLoadingRepositories)
            {
                return Localize("Загрузка репозиториев GitHub");
            }

            if (IsProjectOperationRunning)
            {
                return Localize("Удаление проекта");
            }

            if (IsProjectDetailsLoading)
            {
                return Localize("Загрузка проекта");
            }

            if (IsEnvironmentSaving)
            {
                return Localize("Сохранение переменных окружения");
            }

            if (IsDeploymentRunning)
            {
                return string.IsNullOrWhiteSpace(DeploymentProgressMessage)
                    ? Localize("Выполнение deploy")
                    : DeploymentProgressMessage;
            }

            return ProjectEditor.IsSaving
                ? Localize("Сохранение проекта")
                : Localize("Нет активных операций");
        }
    }

    public async Task LoadSettingsAsync()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        Settings = await _userSettingsService.LoadAsync();
        RemoteMachineArchitecture = FormatArchitecture(
            Settings.RemoteMachine.Architecture);
        AuthenticationMethod = Settings.RemoteMachine.AuthenticationMethod == PasswordAuthentication
            ? Localize(PasswordAuthentication)
            : Localize(PrivateKeyAuthentication);

        GitHubAccountDisplay = string.IsNullOrWhiteSpace(Settings.GitHub.AccountLogin)
            ? Localize("Не подключён")
            : Settings.GitHub.AccountLogin;

        if (_gitHubTokenStore is not null)
        {
            try
            {
                var token = await _gitHubTokenStore.LoadAsync();
                IsGitHubConnected =
                    !string.IsNullOrWhiteSpace(token) &&
                    !string.IsNullOrWhiteSpace(Settings.GitHub.AccountLogin);
            }
            catch (Exception exception)
            {
                GitHubConnectionStatus = exception.Message;
                IsGitHubConnected = false;
            }
        }

        await LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        if (_projectService is null)
        {
            return;
        }

        var projects = await _projectService.GetAllAsync();
        Projects.Clear();

        foreach (var project in projects)
        {
            Projects.Add(project);
        }

        RebuildProjectTiles();
    }

    public async Task LoadProjectDetailsAsync(KKProject project)
    {
        if (_projectEnvironmentService is null ||
            _projectVersionService is null ||
            _projectDeploymentService is null)
        {
            return;
        }

        IsProjectDetailsLoading = true;

        try
        {
            var variablesTask = _projectEnvironmentService.GetAsync(project.Id);
            var versionsTask = _projectVersionService.GetByProjectIdAsync(project.Id);
            var historyTask = _projectDeploymentService.SearchAsync(
                project.Name,
                null,
                null,
                null,
                0,
                HistoryPageSize + 1);
            await Task.WhenAll(variablesTask, versionsTask, historyTask);

            _isLoadingEnvironment = true;
            _environmentAutoSaveCancellation?.Cancel();
            _environmentAutoSaveCancellation?.Dispose();
            _environmentAutoSaveCancellation = null;
            SelectedEnvironmentFileFormat = MapEnvironmentFileFormat(
                project.EnvironmentFileFormat);

            foreach (var variable in EnvironmentVariables)
            {
                variable.PropertyChanged -= EnvironmentVariable_OnPropertyChanged;
            }
            EnvironmentVariables.Clear();
            foreach (var variable in variablesTask.Result)
            {
                var row = new EnvironmentVariableRowViewModel
                {
                    Name = variable.Key,
                    Value = variable.Value,
                };
                row.PropertyChanged += EnvironmentVariable_OnPropertyChanged;
                EnvironmentVariables.Add(row);
            }

            HasUnsavedEnvironmentChanges = false;
            _environmentChangeVersion = 0;
            EnvironmentSaveStatus = Localize("Все изменения сохранены");
            _isLoadingEnvironment = false;

            ProjectVersions.Clear();
            foreach (var version in versionsTask.Result)
            {
                ProjectVersions.Add(new ProjectVersionItemViewModel(
                    version,
                    _localizationService));
            }

            ProjectHistory.Clear();
            HasMoreProjectHistoryItems = historyTask.Result.Count > HistoryPageSize;
            foreach (var deployment in historyTask.Result.Take(HistoryPageSize))
            {
                deployment.Project = project;
                ProjectHistory.Add(new DeploymentHistoryItemViewModel(
                    deployment,
                    _localizationService));
            }

            OnPropertyChanged(nameof(HasNoProjectVersions));
            OnPropertyChanged(nameof(HasNoProjectHistory));
            if (_deploymentEditorProjectId != project.Id)
            {
                DeploymentVersionTag = $"release-{DateTime.Now:yyyyMMdd-HHmmss}";
                DeploymentDescription = string.Empty;
                _deploymentEditorProjectId = project.Id;
            }
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
        }
        finally
        {
            _isLoadingEnvironment = false;
            IsProjectDetailsLoading = false;
        }
    }

    public async Task LoadMoreProjectHistoryAsync()
    {
        if (_projectDeploymentService is null || SelectedProject is null)
        {
            return;
        }

        try
        {
            var deployments = await _projectDeploymentService.SearchAsync(
                SelectedProject.Name,
                null,
                null,
                null,
                ProjectHistory.Count,
                HistoryPageSize + 1);

            HasMoreProjectHistoryItems = deployments.Count > HistoryPageSize;
            foreach (var deployment in deployments.Take(HistoryPageSize))
            {
                ProjectHistory.Add(new DeploymentHistoryItemViewModel(
                    deployment,
                    _localizationService));
            }

            OnPropertyChanged(nameof(HasNoProjectHistory));
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
        }
    }

    public async Task<bool> DeploySelectedProjectAsync()
    {
        if (_projectDeploymentService is null ||
            SelectedProject is null ||
            IsDeploymentRunning)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(DeploymentVersionTag))
        {
            PublishNotification(Localize("Укажите тег новой версии."), isError: true);
            return false;
        }

        IsDeploymentRunning = true;
        DeploymentProgressPercentage = 0;
        DeploymentLogText = string.Empty;
        var progress = new Progress<DeploymentProgress>(HandleDeploymentProgress);

        try
        {
            var deployedTag = DeploymentVersionTag.Trim();
            await _projectDeploymentService.DeployAsync(
                new DeploymentRequest(
                    SelectedProject.Id,
                    deployedTag,
                    DeploymentDescription),
                progress);
            PublishNotification(
                LocalizeFormat("Версия «{0}» успешно развёрнута", deployedTag),
                isError: false);
            await ReloadSelectedProjectAsync(SelectedProject.Id);
            return true;
        }
        catch (Exception exception)
        {
            AppendDeploymentLog(LocalizeFormat("ОШИБКА: {0}", exception.Message));
            PublishNotification(exception.Message, isError: true);
            return false;
        }
        finally
        {
            IsDeploymentRunning = false;
            DeploymentProgressPercentage = 0;
            DeploymentProgressMessage = string.Empty;
        }
    }

    public async Task<bool> RollbackAsync(ProjectVersionItemViewModel item)
    {
        if (_projectDeploymentService is null ||
            SelectedProject is null ||
            IsDeploymentRunning)
        {
            return false;
        }

        IsDeploymentRunning = true;
        DeploymentProgressPercentage = 0;
        DeploymentLogText = string.Empty;
        var progress = new Progress<DeploymentProgress>(HandleDeploymentProgress);

        try
        {
            await _projectDeploymentService.RollbackAsync(
                SelectedProject.Id,
                item.Version.Id,
                progress);
            PublishNotification(
                LocalizeFormat("Выполнен rollback на версию «{0}»", item.Tag),
                isError: false);
            await ReloadSelectedProjectAsync(SelectedProject.Id);
            return true;
        }
        catch (Exception exception)
        {
            AppendDeploymentLog(LocalizeFormat("ОШИБКА: {0}", exception.Message));
            PublishNotification(exception.Message, isError: true);
            return false;
        }
        finally
        {
            IsDeploymentRunning = false;
            DeploymentProgressPercentage = 0;
            DeploymentProgressMessage = string.Empty;
        }
    }

    public void AddEnvironmentVariable()
    {
        var variable = new EnvironmentVariableRowViewModel();
        variable.PropertyChanged += EnvironmentVariable_OnPropertyChanged;
        EnvironmentVariables.Add(variable);
        MarkEnvironmentChanged();
    }

    public void RemoveEnvironmentVariable(EnvironmentVariableRowViewModel variable)
    {
        variable.PropertyChanged -= EnvironmentVariable_OnPropertyChanged;
        EnvironmentVariables.Remove(variable);
        MarkEnvironmentChanged();
    }

    public async Task<bool> SaveEnvironmentVariablesAsync()
    {
        _environmentAutoSaveCancellation?.Cancel();
        _environmentAutoSaveCancellation?.Dispose();
        _environmentAutoSaveCancellation = null;

        while (IsEnvironmentSaving)
        {
            await Task.Delay(50);
        }

        return await SaveEnvironmentVariablesCoreAsync(
            showNotification: true,
            CancellationToken.None);
    }

    private async Task<bool> SaveEnvironmentVariablesCoreAsync(
        bool showNotification,
        CancellationToken cancellationToken)
    {
        if (_projectEnvironmentService is null ||
            SelectedProject is null ||
            IsEnvironmentSaving)
        {
            return false;
        }

        IsEnvironmentSaving = true;
        var changeVersion = _environmentChangeVersion;

        try
        {
            if (EnvironmentVariables.Any(item => string.IsNullOrWhiteSpace(item.Name)))
            {
                EnvironmentSaveStatus = Localize("Заполните имя каждой переменной");
                return false;
            }

            var variables = EnvironmentVariables
                .Select(item => new KeyValuePair<string, string>(
                    item.Name.Trim(),
                    item.Value))
                .ToArray();
            var format = MapEnvironmentFileFormat(
                SelectedEnvironmentFileFormat);
            await _projectEnvironmentService.ReplaceAsync(
                SelectedProject.Id,
                format,
                variables,
                cancellationToken);
            SelectedProject.EnvironmentFileFormat = format;
            if (changeVersion == _environmentChangeVersion)
            {
                HasUnsavedEnvironmentChanges = false;
                EnvironmentSaveStatus = Localize("Все изменения сохранены");
            }

            if (showNotification)
            {
                PublishNotification(
                    Localize("Формат и переменные окружения сохранены"),
                    isError: false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            EnvironmentSaveStatus = exception.Message;

            if (showNotification)
            {
                PublishNotification(exception.Message, isError: true);
            }

            return false;
        }
        finally
        {
            IsEnvironmentSaving = false;
        }
    }

    private void EnvironmentVariable_OnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        MarkEnvironmentChanged();
    }

    private void MarkEnvironmentChanged()
    {
        if (_isLoadingEnvironment)
        {
            return;
        }

        HasUnsavedEnvironmentChanges = true;
        _environmentChangeVersion++;
        EnvironmentSaveStatus = Localize("Есть несохранённые изменения");
        _environmentAutoSaveCancellation?.Cancel();
        _environmentAutoSaveCancellation?.Dispose();
        _environmentAutoSaveCancellation = new CancellationTokenSource();
        _ = AutoSaveEnvironmentAsync(_environmentAutoSaveCancellation.Token);
    }

    private async Task AutoSaveEnvironmentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(800, cancellationToken);

            if (EnvironmentVariables.Any(item => string.IsNullOrWhiteSpace(item.Name)))
            {
                EnvironmentSaveStatus = Localize("Заполните имя каждой переменной");
                return;
            }

            while (IsEnvironmentSaving)
            {
                await Task.Delay(100, cancellationToken);
            }

            EnvironmentSaveStatus = Localize("Сохранение...");
            await SaveEnvironmentVariablesCoreAsync(
                showNotification: false,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task DiscardEnvironmentChangesAsync()
    {
        _environmentAutoSaveCancellation?.Cancel();

        while (IsEnvironmentSaving)
        {
            await Task.Delay(50);
        }

        if (SelectedProject is not null)
        {
            await LoadProjectDetailsAsync(SelectedProject);
        }
    }

    public async Task LoadHistoryAsync()
    {
        if (_projectDeploymentService is null)
        {
            return;
        }

        IsProjectDetailsLoading = true;

        try
        {
            HistoryProjectFilters.Clear();
            HistoryProjectFilters.Add(Localize("Все проекты"));
            foreach (var projectName in Projects
                         .Select(project => project.Name)
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                HistoryProjectFilters.Add(projectName);
            }

            if (!HistoryProjectFilters.Contains(SelectedHistoryProject))
            {
                SelectedHistoryProject = Localize("Все проекты");
            }

            await LoadHistoryPageAsync(reset: true, CancellationToken.None);
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
        }
        finally
        {
            IsProjectDetailsLoading = false;
        }
    }

    public async Task LoadMoreHistoryAsync()
    {
        try
        {
            await LoadHistoryPageAsync(reset: false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
        }
    }

    private async Task LoadHistoryPageAsync(bool reset, CancellationToken cancellationToken)
    {
        if (_projectDeploymentService is null)
        {
            return;
        }

        var search = HistorySearchText.Trim();
        DateTime? startedFromUtc = null;
        DateTime? startedBeforeUtc = null;

        if (DateTime.TryParseExact(
                search,
                "dd/MM/yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDate))
        {
            startedFromUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified));
            startedBeforeUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Unspecified));
            search = string.Empty;
        }

        var deployments = await _projectDeploymentService.SearchAsync(
            SelectedHistoryProject == Localize("Все проекты")
                ? null
                : SelectedHistoryProject,
            search,
            startedFromUtc,
            startedBeforeUtc,
            reset ? 0 : HistoryItems.Count,
            HistoryPageSize + 1,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (reset)
        {
            HistoryItems.Clear();
        }

        HasMoreHistoryItems = deployments.Count > HistoryPageSize;
        foreach (var deployment in deployments.Take(HistoryPageSize))
        {
            HistoryItems.Add(new DeploymentHistoryItemViewModel(
                deployment,
                _localizationService));
        }

        OnPropertyChanged(nameof(HasNoHistoryItems));
    }

    private async Task ReloadHistoryAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await LoadHistoryPageAsync(reset: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
        }
    }

    private void ProjectEditor_OnProjectCreated(object? sender, KKProject project)
    {
        Projects.Add(project);
        SelectedProject = project;
        _createdProjectForNavigation = project;
        RebuildProjectTiles();
        PublishNotification(
            LocalizeFormat("Проект «{0}» добавлен", project.Name),
            isError: false);
    }

    public KKProject? TakeCreatedProjectForNavigation()
    {
        var project = _createdProjectForNavigation;
        _createdProjectForNavigation = null;
        return project;
    }

    private void ProjectEditor_OnProjectUpdated(object? sender, KKProject project)
    {
        var index = Projects
            .Select((item, itemIndex) => new { item, itemIndex })
            .FirstOrDefault(entry => entry.item.Id == project.Id)
            ?.itemIndex;

        if (index.HasValue)
        {
            Projects[index.Value] = project;
        }

        SelectedProject = project;
        RebuildProjectTiles();
        PublishNotification(
            LocalizeFormat("Проект «{0}» изменён", project.Name),
            isError: false);
    }

    public async Task<bool> DeleteProjectAsync(KKProject project)
    {
        if (_projectService is null || IsProjectOperationRunning)
        {
            return false;
        }

        IsProjectOperationRunning = true;

        try
        {
            await _projectService.DeleteAsync(project.Id);
            Projects.Remove(project);

            if (SelectedProject?.Id == project.Id)
            {
                SelectedProject = null;
            }

            RebuildProjectTiles();
            PublishNotification(
                LocalizeFormat("Проект «{0}» удалён", project.Name),
                isError: false);
            return true;
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
            return false;
        }
        finally
        {
            IsProjectOperationRunning = false;
        }
    }

    private void RebuildProjectTiles()
    {
        ProjectTiles.Clear();

        foreach (var project in Projects.OrderBy(project => project.Name))
        {
            ProjectTiles.Add(new ProjectTileViewModel(project, _localizationService));
        }

        ProjectTiles.Add(ProjectTileViewModel.AddTile);
    }

    private void ProjectEditor_OnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreateProjectViewModel.ErrorMessage))
        {
            if (string.IsNullOrWhiteSpace(ProjectEditor.ErrorMessage))
            {
                if (IsNotificationError)
                {
                    ClearStatusNotification();
                }
            }
            else
            {
                PublishNotification(ProjectEditor.ErrorMessage, isError: true);
            }
        }

        if (e.PropertyName is nameof(CreateProjectViewModel.IsSaving) or
            nameof(CreateProjectViewModel.IsLoadingRepositories))
        {
            NotifyOperationStateChanged();
        }
    }

    [RelayCommand]
    private async Task ConnectGitHubAsync()
    {
        if (_gitHubService is null ||
            _gitHubTokenStore is null ||
            _userSettingsService is null ||
            IsGitHubAuthorizationPending)
        {
            return;
        }

        _gitHubAuthorizationCancellation?.Dispose();
        _gitHubAuthorizationCancellation = new CancellationTokenSource();
        var cancellationToken = _gitHubAuthorizationCancellation.Token;
        IsGitHubAuthorizationPending = true;
        GitHubConnectionStatus = Localize("Получаем код авторизации...");

        try
        {
            var authorization = await _gitHubService.StartDeviceAuthorizationAsync(
                cancellationToken);
            GitHubUserCode = authorization.UserCode;
            GitHubVerificationUrl = authorization.VerificationUri.ToString();
            GitHubConnectionStatus = Localize(
                "Введите этот код на открывшейся странице GitHub");
            OpenBrowser(authorization.VerificationUri);

            var token = await _gitHubService.WaitForAccessTokenAsync(
                authorization,
                cancellationToken);
            var user = await _gitHubService.GetCurrentUserAsync(
                token,
                cancellationToken);
            await _gitHubTokenStore.SaveAsync(token, cancellationToken);

            try
            {
                await _userSettingsService.SaveGitHubConnectionAsync(
                    user.Login,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }
            catch
            {
                await _gitHubTokenStore.DeleteAsync(cancellationToken);
                throw;
            }

            Settings.GitHub.AccountLogin = user.Login;
            Settings.GitHub.ConnectedAtUtc = DateTimeOffset.UtcNow;
            GitHubAccountDisplay = user.Login;
            GitHubConnectionStatus = Localize("GitHub успешно подключён");
            IsGitHubConnected = true;
            PublishNotification(Localize("GitHub успешно подключён"), isError: false);
        }
        catch (OperationCanceledException)
        {
            GitHubConnectionStatus = Localize("Подключение GitHub отменено");
        }
        catch (Exception exception)
        {
            GitHubConnectionStatus = exception.Message;
            PublishNotification(exception.Message, isError: true);
        }
        finally
        {
            IsGitHubAuthorizationPending = false;
            GitHubUserCode = string.Empty;
            _gitHubAuthorizationCancellation?.Dispose();
            _gitHubAuthorizationCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelGitHubAuthorization()
    {
        _gitHubAuthorizationCancellation?.Cancel();
    }

    [RelayCommand]
    private void OpenGitHubVerificationPage()
    {
        OpenBrowser(new Uri(GitHubVerificationUrl));
    }

    [RelayCommand]
    private async Task DisconnectGitHubAsync()
    {
        if (_gitHubTokenStore is null || _userSettingsService is null)
        {
            return;
        }

        _gitHubAuthorizationCancellation?.Cancel();
        await _gitHubTokenStore.DeleteAsync();
        await _userSettingsService.ClearGitHubConnectionAsync();

        Settings.GitHub = new GitHubSettings();
        GitHubAccountDisplay = Localize("Не подключён");
        GitHubConnectionStatus = Localize("GitHub отключён");
        IsGitHubConnected = false;
        PublishNotification(Localize("GitHub отключён"), isError: false);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        SettingsError = ValidateRemoteMachineSettings();

        if (!string.IsNullOrEmpty(SettingsError))
        {
            SettingsStatus = string.Empty;
            return;
        }

        Settings.IsFirstRunCompleted = true;
        await _userSettingsService.SaveAsync(Settings);
        IsFirstRunSetupRequired = false;
        SettingsStatus = Localize("Настройки сохранены");
    }

    public void RequireFirstRunSetup()
    {
        IsFirstRunSetupRequired = true;
        SettingsError = string.Empty;
        SettingsStatus = string.Empty;
    }

    [RelayCommand]
    private async Task CheckRemoteConnectionAsync()
    {
        if (_remoteConnectionService is null ||
            _userSettingsService is null ||
            IsConnectionCheckRunning)
        {
            return;
        }

        SettingsError = ValidateRemoteMachineSettings();
        SettingsStatus = string.Empty;

        if (!string.IsNullOrEmpty(SettingsError))
        {
            return;
        }

        IsConnectionCheckRunning = true;
        RemoteMachineArchitecture = Localize(
            "Подключение и определение архитектуры...");

        try
        {
            var result = await _remoteConnectionService.CheckAsync(Settings.RemoteMachine);

            if (!result.IsSuccessful)
            {
                SettingsError = result.ErrorMessage;
                RemoteMachineArchitecture = FormatArchitecture(
                    Settings.RemoteMachine.Architecture);
                return;
            }

            Settings.RemoteMachine.Architecture = result.Architecture;
            await _userSettingsService.SaveRemoteMachineArchitectureAsync(
                result.Architecture);
            SettingsError = string.Empty;
            SettingsStatus = Localize("Подключение успешно");
            RemoteMachineArchitecture = FormatArchitecture(result.Architecture);
        }
        finally
        {
            IsConnectionCheckRunning = false;
        }
    }

    public void ClearSettingsStatus()
    {
        SettingsStatus = string.Empty;
        SettingsError = string.Empty;
        ClearStatusNotification();
    }

    public void ClearStatusNotification()
    {
        NotificationMessage = string.Empty;
        IsNotificationError = false;
    }

    public void ShowNotification(string message, bool isError)
    {
        PublishNotification(message, isError);
    }

    public async Task SwitchLanguageAsync()
    {
        if (_localizationService is null || _userSettingsService is null)
        {
            return;
        }

        var authenticationKey = _localizationService.GetKey(AuthenticationMethod);
        var wasAllProjects = SelectedHistoryProject == Localize("Все проекты");
        var nextLanguage = _localizationService.CurrentLanguage == ApplicationLanguage.Russian
            ? ApplicationLanguage.English
            : ApplicationLanguage.Russian;

        _localizationService.SetLanguage(nextLanguage);
        Settings.Language = nextLanguage;
        RefreshAuthenticationMethods();
        AuthenticationMethod = Localize(authenticationKey);
        ProjectEditor.RefreshLocalization();
        RefreshLocalizedState();
        SettingsStatus = string.Empty;
        SettingsError = string.Empty;
        GitHubConnectionStatus = string.Empty;
        ClearStatusNotification();

        if (HistoryProjectFilters.Count > 0)
        {
            HistoryProjectFilters[0] = Localize("Все проекты");
        }

        if (wasAllProjects)
        {
            SelectedHistoryProject = Localize("Все проекты");
        }

        RebuildProjectTiles();
        foreach (var item in HistoryItems)
        {
            item.RefreshLocalization();
        }
        foreach (var item in ProjectHistory)
        {
            item.RefreshLocalization();
        }
        foreach (var item in ProjectVersions)
        {
            item.RefreshLocalization();
        }

        OnPropertyChanged(string.Empty);
        await _userSettingsService.SaveAsync(Settings);
    }

    public void OpenReadme()
    {
        var uri = Settings.Language == ApplicationLanguage.English
            ? new Uri("https://github.com/KavvaiiKira/KK.Var/blob/master/README.en.md")
            : new Uri("https://github.com/KavvaiiKira/KK.Var");

        OpenBrowser(uri);
    }

    public string Localize(string key) => _localizationService?.Get(key) ?? key;

    public string LocalizeFormat(string key, params object?[] arguments) =>
        _localizationService?.Format(key, arguments) ?? string.Format(key, arguments);

    partial void OnSettingsStatusChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (HasSuccessNotification)
            {
                ClearStatusNotification();
            }

            return;
        }

        PublishNotification(value, isError: false);
    }

    partial void OnSettingsErrorChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (HasErrorNotification)
            {
                ClearStatusNotification();
            }

            return;
        }

        PublishNotification(value, isError: true);
    }

    partial void OnIsConnectionCheckRunningChanged(bool value)
    {
        NotifyOperationStateChanged();
    }

    partial void OnIsGitHubAuthorizationPendingChanged(bool value)
    {
        NotifyOperationStateChanged();
    }

    partial void OnIsProjectOperationRunningChanged(bool value)
    {
        NotifyOperationStateChanged();
    }

    partial void OnIsProjectDetailsLoadingChanged(bool value)
    {
        NotifyOperationStateChanged();
    }

    partial void OnIsEnvironmentSavingChanged(bool value)
    {
        NotifyOperationStateChanged();
    }

    partial void OnIsDeploymentRunningChanged(bool value)
    {
        NotifyOperationStateChanged();
    }

    partial void OnDeploymentProgressMessageChanged(string value)
    {
        OnPropertyChanged(nameof(OperationStatusText));
    }

    private void HandleDeploymentProgress(DeploymentProgress progress)
    {
        if (progress.Percentage >= 0)
        {
            DeploymentProgressPercentage = progress.Percentage;
            DeploymentProgressMessage = progress.Message;
            AppendDeploymentLog(progress.Message);
        }

    }

    private void AppendDeploymentLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        var lines = string.IsNullOrEmpty(DeploymentLogText)
            ? new List<string>()
            : DeploymentLogText.Split(Environment.NewLine).ToList();
        lines.Add(line);
        if (lines.Count > 250)
        {
            lines.RemoveRange(0, lines.Count - 250);
        }
        DeploymentLogText = string.Join(Environment.NewLine, lines);
    }

    private async Task ReloadSelectedProjectAsync(Guid projectId)
    {
        await LoadProjectsAsync();
        var project = Projects.FirstOrDefault(item => item.Id == projectId)
            ?? throw new KeyNotFoundException(
                Localize("Проект не найден после обновления."));
        SelectedProject = project;
        await LoadProjectDetailsAsync(project);
    }

    partial void OnHistorySearchTextChanged(string value)
    {
        _historySearchCancellation?.Cancel();
        _historySearchCancellation?.Dispose();
        _historySearchCancellation = new CancellationTokenSource();
        _ = ReloadHistoryAfterDelayAsync(_historySearchCancellation.Token);
    }

    partial void OnSelectedHistoryProjectChanged(string value)
    {
        _historySearchCancellation?.Cancel();
        _historySearchCancellation?.Dispose();
        _historySearchCancellation = new CancellationTokenSource();
        _ = ReloadHistoryAfterDelayAsync(_historySearchCancellation.Token);
    }

    partial void OnSelectedEnvironmentFileFormatChanged(string value)
    {
        MarkEnvironmentChanged();
    }

    partial void OnNotificationMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasNotification));
        OnPropertyChanged(nameof(HasErrorNotification));
        OnPropertyChanged(nameof(HasSuccessNotification));
    }

    partial void OnIsNotificationErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(HasErrorNotification));
        OnPropertyChanged(nameof(HasSuccessNotification));
    }

    private void PublishNotification(string message, bool isError)
    {
        IsNotificationError = isError;
        NotificationMessage = message;
    }

    private void NotifyOperationStateChanged()
    {
        OnPropertyChanged(nameof(IsOperationRunning));
        OnPropertyChanged(nameof(OperationStatusText));
    }

    partial void OnAuthenticationMethodChanged(string value)
    {
        Settings.RemoteMachine.AuthenticationMethod =
            Canonicalize(value) == PasswordAuthentication
                ? PasswordAuthentication
                : PrivateKeyAuthentication;
        OnPropertyChanged(nameof(IsPrivateKeyAuthentication));
        OnPropertyChanged(nameof(IsPasswordAuthentication));
    }

    private void RefreshAuthenticationMethods()
    {
        AuthenticationMethods.Clear();
        AuthenticationMethods.Add(Localize(PrivateKeyAuthentication));
        AuthenticationMethods.Add(Localize(PasswordAuthentication));
    }

    private void RefreshLocalizedState()
    {
        EnvironmentSaveStatus = HasUnsavedEnvironmentChanges
            ? Localize("Есть несохранённые изменения")
            : Localize("Все изменения сохранены");
        RemoteMachineArchitecture = FormatArchitecture(
            Settings.RemoteMachine.Architecture);
        GitHubAccountDisplay = string.IsNullOrWhiteSpace(Settings.GitHub.AccountLogin)
            ? Localize("Не подключён")
            : Settings.GitHub.AccountLogin;
        if (string.IsNullOrWhiteSpace(SelectedHistoryProject))
        {
            SelectedHistoryProject = Localize("Все проекты");
        }
    }

    partial void OnSelectedProjectChanged(KKProject? value)
    {
        OnPropertyChanged(nameof(SelectedProjectSourceDisplay));
        OnPropertyChanged(nameof(SelectedProjectLastDeploymentDisplay));
        OnPropertyChanged(nameof(SelectedProjectLatestVersionTag));
    }

    private string Canonicalize(string value) =>
        _localizationService?.GetKey(value) ?? value;

    private string ValidateRemoteMachineSettings()
    {
        if (string.IsNullOrWhiteSpace(Settings.RemoteMachine.Host))
        {
            return Localize("Укажите адрес удалённой машины.");
        }

        if (Settings.RemoteMachine.Port is < 1 or > 65535)
        {
            return Localize("SSH-порт должен находиться в диапазоне от 1 до 65535.");
        }

        if (string.IsNullOrWhiteSpace(Settings.RemoteMachine.UserName))
        {
            return Localize("Укажите пользователя SSH.");
        }

        if (IsPrivateKeyAuthentication &&
            string.IsNullOrWhiteSpace(Settings.RemoteMachine.PrivateKeyPath))
        {
            return Localize("Укажите путь к приватному SSH-ключу.");
        }

        if (IsPasswordAuthentication &&
            string.IsNullOrWhiteSpace(Settings.RemoteMachine.Password))
        {
            return Localize("Укажите пароль SSH.");
        }

        return string.Empty;
    }

    private string FormatArchitecture(string? architecture) =>
        string.IsNullOrWhiteSpace(architecture)
            ? Localize("Будет определена автоматически при проверке подключения")
            : LocalizeFormat("Определена автоматически: {0}", architecture);

    private static string MapEnvironmentFileFormat(EnvironmentFileFormat format) =>
        format switch
        {
            EnvironmentFileFormat.DotEnv => DotEnvEnvironmentFormat,
            EnvironmentFileFormat.Json => JsonEnvironmentFormat,
            EnvironmentFileFormat.Shell => ShellEnvironmentFormat,
            EnvironmentFileFormat.Yaml => YamlEnvironmentFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static EnvironmentFileFormat MapEnvironmentFileFormat(string format) =>
        format switch
        {
            DotEnvEnvironmentFormat => EnvironmentFileFormat.DotEnv,
            JsonEnvironmentFormat => EnvironmentFileFormat.Json,
            ShellEnvironmentFormat => EnvironmentFileFormat.Shell,
            YamlEnvironmentFormat => EnvironmentFileFormat.Yaml,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });
    }
}
