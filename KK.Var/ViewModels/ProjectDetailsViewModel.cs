using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class ProjectDetailsViewModel(
        IKKProjectVersionService _projectVersionService,
        IDeploymentHistoryService _deploymentHistory,
        IArtifactStorageService _artifactStorageService,
        IVersionCleanupService _versionCleanupService,
        ProjectEnvironmentEditorViewModel environmentEditor,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        private int _projectDetailsRevision;

        private int _activeProjectDetailsLoads;

        private bool _isLoadingMoreHistory;

        private KKProject? SelectedProject { get; set; }

        public void SelectProject(KKProject? project)
        {
            _projectDetailsRevision++;
            SelectedProject = project;
            PendingVersionCleanup = null;
        }

        public ObservableCollection<ProjectVersionItemViewModel> ProjectVersions { get; } = [];

        public ObservableCollection<DeploymentHistoryItemViewModel> ProjectHistory { get; } = [];

        [ObservableProperty]
        public partial bool IsProjectDetailsLoading { get; private set; }

        [ObservableProperty]
        public partial long ProjectArtifactsSizeBytes { get; private set; }

        [ObservableProperty]
        public partial int MissingProjectArtifactCount { get; private set; }

        [ObservableProperty]
        public partial bool HasMoreProjectHistoryItems { get; private set; }

        public bool HasNoProjectVersions => ProjectVersions.Count == 0;

        public string ProjectArtifactsSizeDisplay => ByteSizeFormatter.Format(
            ProjectArtifactsSizeBytes,
            _localizationService);

        public bool HasMissingProjectArtifacts => MissingProjectArtifactCount > 0;

        public string MissingProjectArtifactsDisplay =>
            LocalizeFormat(Texts.MissingArchives, MissingProjectArtifactCount);

        public bool HasNoProjectHistory => ProjectHistory.Count == 0;

        public async Task<bool> LoadAsync(KKProject project)
        {
            var revision = ++_projectDetailsRevision;
            _activeProjectDetailsLoads++;
            IsProjectDetailsLoading = true;

            try
            {
                var variablesTask = environmentEditor.ReadAsync(project.Id);
                var versionsTask = _projectVersionService.GetByProjectIdAsync(project.Id);
                var protectedVersionsTask = _projectVersionService.GetProtectedVersionIdsAsync(
                    project.Id);
                var historyTask = _deploymentHistory.SearchAsync(
                    project.Name,
                    null,
                    null,
                    null,
                    null,
                    0,
                    DeploymentHistoryViewModel.PageSize + 1);
                await Task.WhenAll(
                    variablesTask,
                    versionsTask,
                    protectedVersionsTask,
                    historyTask);

                var artifactStates = await Task.WhenAll(versionsTask.Result.Select(version =>
                    _artifactStorageService.GetFileStateAsync(version.ArtifactRelativePath)));

                if (revision != _projectDetailsRevision || !ReferenceEquals(SelectedProject, project))
                {
                    return false;
                }

                environmentEditor.Load(project, variablesTask.Result);
                ProjectVersions.Clear();

                ProjectArtifactsSizeBytes = artifactStates
                    .Where(state => state.Exists)
                    .Sum(state => state.Size);
                MissingProjectArtifactCount = artifactStates.Count(state => !state.Exists);

                for (var index = 0; index < versionsTask.Result.Count; index++)
                {
                    ProjectVersions.Add(new ProjectVersionItemViewModel(
                        versionsTask.Result[index],
                        _localizationService,
                        artifactStates[index].Exists,
                        protectedVersionsTask.Result.Contains(versionsTask.Result[index].Id),
                        artifactStates[index].Size));
                }

                ProjectHistory.Clear();
                HasMoreProjectHistoryItems = historyTask.Result.Count > DeploymentHistoryViewModel.PageSize;

                foreach (var deployment in historyTask.Result.Take(DeploymentHistoryViewModel.PageSize))
                {
                    deployment.Project = project;
                    ProjectHistory.Add(new DeploymentHistoryItemViewModel(
                        deployment,
                        _localizationService));
                }

                OnPropertyChanged(nameof(HasNoProjectVersions));
                OnPropertyChanged(nameof(ProjectArtifactsSizeDisplay));
                OnPropertyChanged(nameof(HasMissingProjectArtifacts));
                OnPropertyChanged(nameof(MissingProjectArtifactsDisplay));
                OnPropertyChanged(nameof(HasNoProjectHistory));
                return true;
            }
            catch (Exception exception)
            {
                if (revision == _projectDetailsRevision)
                {
                    PublishNotification(Localize(exception.Message), isError: true);
                }
                return false;
            }
            finally
            {
                _activeProjectDetailsLoads--;
                IsProjectDetailsLoading = _activeProjectDetailsLoads > 0;
            }
        }

        public async Task LoadMoreProjectHistoryAsync()
        {
            if (SelectedProject is null || _isLoadingMoreHistory || IsProjectDetailsLoading)
            {
                return;
            }

            var project = SelectedProject;
            var revision = _projectDetailsRevision;
            _isLoadingMoreHistory = true;

            try
            {
                var deployments = await _deploymentHistory.SearchAsync(
                    project.Name,
                    null,
                    null,
                    null,
                    null,
                    ProjectHistory.Count,
                    DeploymentHistoryViewModel.PageSize + 1);

                if (revision != _projectDetailsRevision)
                {
                    return;
                }

                HasMoreProjectHistoryItems = deployments.Count > DeploymentHistoryViewModel.PageSize;

                foreach (var deployment in deployments.Take(DeploymentHistoryViewModel.PageSize))
                {
                    ProjectHistory.Add(new DeploymentHistoryItemViewModel(
                        deployment,
                        _localizationService));
                }

                OnPropertyChanged(nameof(HasNoProjectHistory));
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);
            }
            finally
            {
                _isLoadingMoreHistory = false;
            }
        }

        [ObservableProperty]
        public partial VersionCleanupPlan? PendingVersionCleanup { get; private set; }

        [ObservableProperty]
        public partial bool IsVersionCleanupBusy { get; private set; }

        public bool HasPendingVersionCleanup => PendingVersionCleanup is not null;

        public string VersionCleanupPreview => PendingVersionCleanup is { } plan ?
            LocalizeFormat(
                Texts.VersionsDeleteBytesActionCannotUndone,
                plan.VersionCount,
                plan.BytesToDelete.ToString("N0")) :
            string.Empty;

        public async Task ToggleVersionPinnedAsync(ProjectVersionItemViewModel item)
        {
            if (SelectedProject is null)
            {
                return;
            }

            var project = SelectedProject;

            try
            {
                await _projectVersionService.SetPinnedAsync(
                    item.Version.Id,
                    !item.Version.IsPinned);
                if (ReferenceEquals(SelectedProject, project))
                {
                    await LoadAsync(project);
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(SelectedProject, project))
                {
                    PublishNotification(Localize(exception.Message), isError: true);
                }
            }
        }

        public async Task DeleteVersionAsync(ProjectVersionItemViewModel item)
        {
            if (SelectedProject is null)
            {
                return;
            }

            var project = SelectedProject;

            try
            {
                await _projectVersionService.DeleteAsync(item.Version.Id);
                if (ReferenceEquals(SelectedProject, project))
                {
                    PublishNotification(
                        LocalizeFormat(Texts.VersionDeleted, item.Tag),
                        isError: false);
                    await LoadAsync(project);
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(SelectedProject, project))
                {
                    PublishNotification(Localize(exception.Message), isError: true);
                }
            }
        }

        public async Task PrepareVersionCleanupAsync()
        {
            if (SelectedProject is null ||
                IsVersionCleanupBusy)
            {
                return;
            }

            var project = SelectedProject;
            var revision = _projectDetailsRevision;
            IsVersionCleanupBusy = true;

            try
            {
                var plan = await _versionCleanupService.PlanAsync(project.Id);
                if (revision != _projectDetailsRevision || !ReferenceEquals(SelectedProject, project))
                {
                    return;
                }
                PendingVersionCleanup = plan.VersionCount == 0 ? null : plan;

                if (plan.VersionCount == 0)
                {
                    PublishNotification(Texts.NoVersionCleanupNeeded, isError: false);
                }
            }
            catch (Exception exception)
            {
                PendingVersionCleanup = null;
                PublishNotification(Localize(exception.Message), isError: true);
            }
            finally
            {
                IsVersionCleanupBusy = false;
            }
        }

        public async Task ApplyVersionCleanupAsync()
        {
            if (PendingVersionCleanup is not { } plan ||
                SelectedProject?.Id != plan.ProjectId ||
                IsVersionCleanupBusy)
            {
                return;
            }

            IsVersionCleanupBusy = true;
            PendingVersionCleanup = null;

            var project = SelectedProject!;

            try
            {
                var result = await _versionCleanupService.ExecuteAsync(
                    plan.ProjectId,
                    expectedPlan: plan);
                if (ReferenceEquals(SelectedProject, project))
                {
                    await LoadAsync(project);
                }
                if (ReferenceEquals(SelectedProject, project))
                {
                    PublishNotification(LocalizeFormat(
                        Texts.DeletedVersionsBytes,
                        result.VersionCount,
                        result.BytesToDelete.ToString("N0")), isError: false);
                }
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(SelectedProject, project))
                {
                    await LoadAsync(project);
                }
                if (ReferenceEquals(SelectedProject, project))
                {
                    PublishNotification(Localize(exception.Message), isError: true);
                }
            }
            finally
            {
                IsVersionCleanupBusy = false;
            }
        }

        public void CancelVersionCleanup() => PendingVersionCleanup = null;

        partial void OnPendingVersionCleanupChanged(VersionCleanupPlan? value)
        {
            OnPropertyChanged(nameof(HasPendingVersionCleanup));
            OnPropertyChanged(nameof(VersionCleanupPreview));
        }
    }
}
