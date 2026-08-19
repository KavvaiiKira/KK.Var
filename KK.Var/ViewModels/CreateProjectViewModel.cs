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

    private readonly IKKProjectService? _projectService;
    private readonly IGitHubService? _gitHubService;
    private readonly IGitHubTokenStore? _gitHubTokenStore;
    private bool _isResetting;
    private Guid? _editingProjectId;
    private string _buildConfigurationJson = "{}";
    private KKProject? _editingProject;

    public CreateProjectViewModel()
    {
    }

    public CreateProjectViewModel(
        IKKProjectService projectService,
        IGitHubService gitHubService,
        IGitHubTokenStore gitHubTokenStore)
    {
        _projectService = projectService;
        _gitHubService = gitHubService;
        _gitHubTokenStore = gitHubTokenStore;
    }

    public event EventHandler<KKProject>? ProjectCreated;

    public event EventHandler<KKProject>? ProjectUpdated;

    public IReadOnlyList<string> SourceTypes { get; } =
        [LocalSource, GitHubSource];

    public IReadOnlyList<string> BuildProviders { get; } =
        [AutomaticBuild, ".NET", "Python", "Go", "C++", "Свой сценарий"];

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
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingRepositories { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool IsLocalSource => SelectedSourceType == LocalSource;

    public bool IsGitHubSource => SelectedSourceType == GitHubSource;

    public bool IsEditing => _editingProjectId.HasValue;

    public string EditorTitle => IsEditing ? "Редактирование проекта" : "Новый проект";

    public string SaveButtonText => IsEditing ? "Сохранить изменения" : "Сохранить проект";

    public void Reset()
    {
        _isResetting = true;
        _editingProjectId = null;
        _buildConfigurationJson = "{}";
        _editingProject = null;

        Name = string.Empty;
        Description = string.Empty;
        SelectedSourceType = LocalSource;
        LocalDirectoryPath = string.Empty;
        SelectedGitHubRepository = null;
        SelectedBuildProvider = AutomaticBuild;
        RemoteServiceName = string.Empty;
        RemoteExecutableFileName = string.Empty;
        RemoteDeploymentDirectory = string.Empty;
        ProjectEnvironmentFilePath = string.Empty;
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
        _buildConfigurationJson = project.BuildConfigurationJson;
        _editingProject = project;

        Name = project.Name;
        Description = project.Description ?? string.Empty;
        SelectedSourceType = project.SourceType == ProjectSourceType.GitHubRepository
            ? GitHubSource
            : LocalSource;
        LocalDirectoryPath = project.LocalDirectoryPath ?? string.Empty;
        SelectedGitHubRepository = project.SourceType == ProjectSourceType.GitHubRepository &&
                                   project.GitHubRepositoryId.HasValue &&
                                   project.GitHubRepositoryFullName is not null &&
                                   project.GitHubCloneUrl is not null
            ? new GitHubRepository(
                project.GitHubRepositoryId.Value,
                project.GitHubRepositoryFullName.Split('/')[^1],
                project.GitHubRepositoryFullName,
                project.GitHubCloneUrl,
                string.Empty,
                false)
            : null;
        SelectedBuildProvider = MapBuildProvider(project.BuildProvider);
        RemoteServiceName = project.RemoteServiceName;
        RemoteExecutableFileName = project.RemoteExecutableFileName;
        RemoteDeploymentDirectory = project.RemoteDeploymentDirectory;
        ProjectEnvironmentFilePath = project.ProjectEnvironmentFilePath;
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
            SourceType = IsLocalSource
                ? ProjectSourceType.LocalDirectory
                : ProjectSourceType.GitHubRepository,
            LocalDirectoryPath = IsLocalSource ? LocalDirectoryPath : null,
            GitHubRepositoryId = IsGitHubSource ? SelectedGitHubRepository?.Id : null,
            GitHubRepositoryFullName = IsGitHubSource
                ? SelectedGitHubRepository?.FullName
                : null,
            GitHubCloneUrl = IsGitHubSource ? SelectedGitHubRepository?.CloneUrl : null,
            BuildProvider = MapBuildProvider(),
            BuildConfigurationJson = _buildConfigurationJson,
            RemoteServiceName = RemoteServiceName,
            RemoteExecutableFileName = RemoteExecutableFileName,
            RemoteDeploymentDirectory = RemoteDeploymentDirectory,
            ProjectEnvironmentFilePath = ProjectEnvironmentFilePath,
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
        if (_gitHubService is null || _gitHubTokenStore is null || IsLoadingRepositories)
        {
            return;
        }

        IsLoadingRepositories = true;
        ErrorMessage = string.Empty;

        try
        {
            var token = await _gitHubTokenStore.LoadAsync();

            if (string.IsNullOrWhiteSpace(token))
            {
                ErrorMessage = "Сначала подключите GitHub на странице настроек.";
                return;
            }

            var selectedRepositoryId = SelectedGitHubRepository?.Id;
            var repositories = await _gitHubService.GetRepositoriesAsync(token);
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

        if (value == GitHubSource && GitHubRepositories.Count == 0)
        {
            _ = RefreshGitHubRepositoriesAsync();
        }
    }

    partial void OnLocalDirectoryPathChanged(string value) => MarkDirty();

    partial void OnSelectedGitHubRepositoryChanged(GitHubRepository? value) => MarkDirty();

    partial void OnSelectedBuildProviderChanged(string value) => MarkDirty();

    partial void OnRemoteServiceNameChanged(string value) => MarkDirty();

    partial void OnRemoteExecutableFileNameChanged(string value) => MarkDirty();

    partial void OnRemoteDeploymentDirectoryChanged(string value) => MarkDirty();

    partial void OnProjectEnvironmentFilePathChanged(string value) => MarkDirty();

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
            return "Укажите название проекта.";
        }

        if (IsLocalSource && string.IsNullOrWhiteSpace(LocalDirectoryPath))
        {
            return "Выберите локальную папку проекта.";
        }

        if (IsGitHubSource && SelectedGitHubRepository is null)
        {
            return "Выберите репозиторий GitHub.";
        }

        if (string.IsNullOrWhiteSpace(RemoteServiceName))
        {
            return "Укажите название systemd-сервиса.";
        }

        if (string.IsNullOrWhiteSpace(RemoteExecutableFileName))
        {
            return "Укажите исполняемый файл или точку входа.";
        }

        if (string.IsNullOrWhiteSpace(RemoteDeploymentDirectory))
        {
            return "Укажите директорию развёртывания на удалённой машине.";
        }

        if (string.IsNullOrWhiteSpace(ProjectEnvironmentFilePath))
        {
            return "Укажите путь к файлу переменных окружения внутри проекта.";
        }

        return string.Empty;
    }

    private ProjectBuildProvider MapBuildProvider() => SelectedBuildProvider switch
    {
        ".NET" => ProjectBuildProvider.DotNet,
        "Python" => ProjectBuildProvider.Python,
        "Go" => ProjectBuildProvider.Go,
        "C++" => ProjectBuildProvider.Cpp,
        "Свой сценарий" => ProjectBuildProvider.Custom,
        _ => ProjectBuildProvider.Unknown,
    };

    private static string MapBuildProvider(ProjectBuildProvider provider) => provider switch
    {
        ProjectBuildProvider.DotNet => ".NET",
        ProjectBuildProvider.Python => "Python",
        ProjectBuildProvider.Go => "Go",
        ProjectBuildProvider.Cpp => "C++",
        ProjectBuildProvider.Custom => "Свой сценарий",
        _ => AutomaticBuild,
    };
}
