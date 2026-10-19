using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public partial class MainViewModel
{
    private readonly IProjectConfigurationTransferService? _projectConfigurationTransferService;
    private ProjectConfigurationDocument? _pendingProjectImport;
    private bool _isPreparingProjectImport;

    [ObservableProperty]
    public partial bool HasPendingProjectImport { get; set; }

    [ObservableProperty]
    public partial bool IsProjectTransferBusy { get; set; }

    [ObservableProperty]
    public partial string ImportProjectName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportLocalDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportServiceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportDeploymentDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportError { get; set; } = string.Empty;

    public bool IsImportLocalSource => _pendingProjectImport?.Project?.SourceType == ProjectSourceType.LocalDirectory;

    public bool CanExportProjectConfiguration =>
        SelectedProject is not null && !IsProjectTransferBusy && !IsEnvironmentSaving;

    public string ImportPreviewJson => _pendingProjectImport is null ? string.Empty :
        _projectConfigurationTransferService?.Describe(_pendingProjectImport) ?? string.Empty;

    public void PrepareProjectImport(string json)
    {
        if (_projectConfigurationTransferService is null || IsProjectTransferBusy)
        {
            return;
        }
        try
        {
            _pendingProjectImport = _projectConfigurationTransferService.Read(json);
            var data = _pendingProjectImport.Project!;
            _isPreparingProjectImport = true;
            ImportProjectName = data.Name;
            ImportLocalDirectory = data.LocalDirectoryPath ?? string.Empty;
            ImportServiceName = data.RemoteServiceName;
            ImportDeploymentDirectory = data.RemoteDeploymentDirectory;
            _isPreparingProjectImport = false;
            ImportError = string.Empty;
            HasPendingProjectImport = true;
            OnPropertyChanged(nameof(IsImportLocalSource));
            OnPropertyChanged(nameof(ImportPreviewJson));
        }
        catch (Exception exception)
        {
            _isPreparingProjectImport = false;
            PublishNotification(exception.Message, isError: true);
        }
    }

    public void CancelProjectImport()
    {
        if (IsProjectTransferBusy)
        {
            return;
        }
        HasPendingProjectImport = false;
        _pendingProjectImport = null;
        OnPropertyChanged(nameof(ImportPreviewJson));
        ImportError = string.Empty;
    }

    public async Task<KKProject?> ConfirmProjectImportAsync()
    {
        if (_projectConfigurationTransferService is null || _pendingProjectImport is null || IsProjectTransferBusy)
        {
            return null;
        }
        IsProjectTransferBusy = true;
        ImportError = string.Empty;
        try
        {
            UpdateImportPreview();
            var project = await _projectConfigurationTransferService.ImportAsync(_pendingProjectImport);
            Projects.Add(project);
            SelectedProject = project;
            RebuildProjectTiles();
            HasPendingProjectImport = false;
            _pendingProjectImport = null;
            OnPropertyChanged(nameof(ImportPreviewJson));
            PublishNotification(LocalizeFormat("Проект «{0}» импортирован.", project.Name), isError: false);
            return project;
        }
        catch (Exception exception)
        {
            ImportError = exception.Message;
            return null;
        }
        finally
        {
            IsProjectTransferBusy = false;
        }
    }

    public async Task<string?> ExportProjectConfigurationAsync()
    {
        if (_projectConfigurationTransferService is null || SelectedProject is null || IsProjectTransferBusy)
        {
            return null;
        }
        IsProjectTransferBusy = true;
        var projectId = SelectedProject.Id;
        try
        {
            if (HasUnsavedEnvironmentChanges && !await SaveEnvironmentVariablesAsync())
            {
                return null;
            }
            return await _projectConfigurationTransferService.ExportAsync(projectId);
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
            return null;
        }
        finally
        {
            IsProjectTransferBusy = false;
        }
    }

    private void UpdateImportPreview()
    {
        if (_isPreparingProjectImport || _pendingProjectImport?.Project is not { } data)
        {
            return;
        }
        data.Name = ImportProjectName;
        data.LocalDirectoryPath = IsImportLocalSource ? ImportLocalDirectory : null;
        data.RemoteServiceName = ImportServiceName;
        data.RemoteDeploymentDirectory = ImportDeploymentDirectory;
        ImportError = string.Empty;
        OnPropertyChanged(nameof(ImportPreviewJson));
    }

    partial void OnImportProjectNameChanged(string value) => UpdateImportPreview();

    partial void OnImportLocalDirectoryChanged(string value) => UpdateImportPreview();

    partial void OnImportServiceNameChanged(string value) => UpdateImportPreview();

    partial void OnImportDeploymentDirectoryChanged(string value) => UpdateImportPreview();

    partial void OnIsProjectTransferBusyChanged(bool value) =>
        OnPropertyChanged(nameof(CanExportProjectConfiguration));
}
