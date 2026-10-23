using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        private KKProject? _createdProjectForNavigation;

        [ObservableProperty]
        public partial KKProject? SelectedProject { get; set; }

        public string SelectedProjectName => SelectedProject?.Name ?? string.Empty;

        public string SelectedProjectDescription => SelectedProject?.EffectiveDescription ?? string.Empty;

        public string SelectedProjectBuildProvider =>
            ProjectEditor.BuildProviders.FirstOrDefault(option => Equals(option.Value, SelectedProject?.BuildProvider))?.Label ??
            string.Empty;

        public string SelectedProjectRemoteServiceName => SelectedProject?.RemoteServiceName ?? string.Empty;

        public string SelectedProjectRemoteExecutablePath => SelectedProject?.RemoteExecutablePath ?? string.Empty;

        public string SelectedProjectEnvironmentFilePath => SelectedProject?.ProjectEnvironmentFilePath ?? string.Empty;

        public string SelectedProjectSourceDisplay =>
            SelectedProject is null ?
                string.Empty :
                SelectedProject.SourceType == ProjectSourceType.GitHubRepository ?
                    $"GitHub · {SelectedProject.GitHubRepositoryFullName}" :
                    $"{Texts.LocalFolder} · {SelectedProject.LocalDirectoryPath}";

        public string SelectedProjectLastDeploymentDisplay
        {
            get
            {
                var deployment = SelectedProject?.Deployments.MaxBy(item => item.StartedAtUtc);
                return deployment is null ?
                    Texts.Never :
                    deployment.StartedAtUtc.ToLocalTime().ToString("g");
            }
        }

        public string SelectedProjectLatestVersionTag =>
            SelectedProject?.Versions.MaxBy(version => version.CreatedAtUtc)?.Tag ?? Texts.NoVersions;

        public async Task LoadProjectDetailsAsync(KKProject project)
        {
            if (!await ProjectDetails.LoadAsync(project) || !ReferenceEquals(SelectedProject, project))
            {
                return;
            }

            if (DeploymentWorkspace.TryGetState(project.Id, out var deploymentState) &&
                deploymentState.IsActive)
            {
                DeploymentVersionTag = deploymentState.VersionTag;
                DeploymentDescription = deploymentState.Description;
                _deploymentEditorProjectId = project.Id;
            }
            else if (_deploymentEditorProjectId != project.Id)
            {
                DeploymentVersionTag = $"release-{DateTime.Now:yyyyMMdd-HHmmss}";
                DeploymentDescription = string.Empty;
                _deploymentEditorProjectId = project.Id;
            }
        }

        private void ProjectEditor_OnProjectCreated(object? sender, KKProject project)
        {
            _ = MachineEditor.ReloadMachineProfilesAsync();
            ProjectList.Add(project);
            SelectedProject = project;
            _createdProjectForNavigation = project;
        }

        public KKProject? TakeCreatedProjectForNavigation()
        {
            var project = _createdProjectForNavigation;

            _createdProjectForNavigation = null;

            return project;
        }

        private void ProjectEditor_OnProjectUpdated(object? sender, KKProject project)
        {
            _ = MachineEditor.ReloadMachineProfilesAsync();
            InvalidateDeploymentPreflight(project.Id);
            ProjectList.Update(project);
            SelectedProject = project;
        }

        public async Task<bool> DeleteProjectAsync(KKProject project)
        {
            if (!await ProjectList.DeleteProjectAsync(project))
            {
                return false;
            }
            if (SelectedProject?.Id == project.Id)
            {
                SelectedProject = null;
            }
            return true;
        }

        private void ProjectEditor_OnPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (ProjectEditor.IsEditing &&
                e.PropertyName is nameof(CreateProjectViewModel.BuildConfigurationJson) or
                    nameof(CreateProjectViewModel.SelectedBuildProvider))
            {
                InvalidateDeploymentPreflight(SelectedProject?.Id);
            }

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

        private async Task ReloadSelectedProjectAsync(Guid projectId)
        {
            await ProjectList.LoadAsync();

            var project = ProjectList.Projects.FirstOrDefault(item => item.Id == projectId) ??
                throw new KeyNotFoundException(Texts.ProjectNotFoundRefresh);

            if (SelectedProject?.Id == projectId)
            {
                SelectedProject = project;
                await LoadProjectDetailsAsync(project);
            }
        }

        partial void OnSelectedProjectChanged(KKProject? value)
        {
            EnvironmentEditor.SelectProject(value);
            ProjectDetails.SelectProject(value);
            DeploymentWorkspace.SelectedProjectId = value?.Id;
            OnPropertyChanged(nameof(CanExportProjectConfiguration));
            ConfigureDeploymentGitHubSource(value);
            OnPropertyChanged(nameof(SelectedProjectSourceDisplay));
            OnPropertyChanged(nameof(SelectedProjectName));
            OnPropertyChanged(nameof(SelectedProjectDescription));
            OnPropertyChanged(nameof(SelectedProjectBuildProvider));
            OnPropertyChanged(nameof(SelectedProjectRemoteServiceName));
            OnPropertyChanged(nameof(SelectedProjectRemoteExecutablePath));
            OnPropertyChanged(nameof(SelectedProjectEnvironmentFilePath));
            OnPropertyChanged(nameof(SelectedProjectLastDeploymentDisplay));
            OnPropertyChanged(nameof(SelectedProjectLatestVersionTag));
            DeploymentWorkspace.NotifySelectedDeploymentStateChanged();
        }
    }
}
