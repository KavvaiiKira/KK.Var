using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        private readonly IProjectConfigurationTransferService _projectConfigurationTransferService;

        private ProjectConfigurationDocument? _pendingProjectImport;

        private bool _isPreparingProjectImport;

        [ObservableProperty]
        public partial RemoteMachine? SelectedImportMachine { get; set; }

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
            SelectedProject is not null && !IsProjectTransferBusy && !EnvironmentEditor.IsEnvironmentSaving;

        public string ImportPreviewJson => _pendingProjectImport is null ?
            string.Empty :
            _projectConfigurationTransferService.Describe(_pendingProjectImport);

        public void PrepareProjectImport(string json)
        {
            if (IsProjectTransferBusy)
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
                SelectedImportMachine = MachineEditor.AvailableMachines.FirstOrDefault();
                HasPendingProjectImport = true;
                OnPropertyChanged(nameof(IsImportLocalSource));
                OnPropertyChanged(nameof(ImportPreviewJson));
            }
            catch (Exception exception)
            {
                _isPreparingProjectImport = false;
                PublishNotification(Localize(exception.Message), isError: true);
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
            if (_pendingProjectImport is null || IsProjectTransferBusy)
            {
                return null;
            }
            IsProjectTransferBusy = true;
            ImportError = string.Empty;
            try
            {
                UpdateImportPreview();
                if (SelectedImportMachine is null)
                {
                    throw new InvalidOperationException(Localize("Выберите проверенный профиль машины."));
                }
                var project = await _projectConfigurationTransferService.ImportAsync(
                    _pendingProjectImport,
                    SelectedImportMachine.Id);
                ProjectList.Projects.Add(project);
                SelectedProject = project;
                ProjectList.RefreshLocalization();
                HasPendingProjectImport = false;
                _pendingProjectImport = null;
                OnPropertyChanged(nameof(ImportPreviewJson));
                PublishNotification(LocalizeFormat("Проект «{0}» импортирован.", project.Name), isError: false);
                return project;
            }
            catch (Exception exception)
            {
                ImportError = Localize(exception.Message);
                return null;
            }
            finally
            {
                IsProjectTransferBusy = false;
            }
        }

        public async Task<string?> ExportProjectConfigurationAsync()
        {
            if (SelectedProject is null || IsProjectTransferBusy)
            {
                return null;
            }
            IsProjectTransferBusy = true;
            var projectId = SelectedProject.Id;
            try
            {
                if (EnvironmentEditor.HasUnsavedEnvironmentChanges && !await EnvironmentEditor.SaveEnvironmentVariablesAsync())
                {
                    return null;
                }
                return await _projectConfigurationTransferService.ExportAsync(projectId);
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);
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

        partial void OnIsProjectTransferBusyChanged(bool value)
        {
            OnPropertyChanged(nameof(CanExportProjectConfiguration));
            NotifyOperationStateChanged();
        }
    }
}
