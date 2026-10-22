using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Data;
using KK.Var.Enums;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        private Guid? _deploymentEditorProjectId;

        private int _deploymentDetailsRevision;

        private VersionTagError? _versionTagError;

        private string? _failedVersionTag;

        [ObservableProperty]
        public partial DeploymentDetailsViewModel? SelectedDeploymentDetails { get; set; }

        [ObservableProperty]
        public partial string DeploymentVersionTag { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string DeploymentDescription { get; set; } = string.Empty;

        public async Task LoadDeploymentDetailsAsync(DeploymentHistoryItemViewModel item)
        {
            var revision = ++_deploymentDetailsRevision;
            var logText = await ReadDeploymentLogAsync(item.Deployment.LogPath);
            if (revision != _deploymentDetailsRevision)
            {
                return;
            }
            SelectedDeploymentDetails = new DeploymentDetailsViewModel(
                item.Deployment,
                logText,
                _localizationService);
        }

        public void ClearDeploymentDetails()
        {
            _deploymentDetailsRevision++;
            SelectedDeploymentDetails = null;
        }

        public void PrepareRepeatDeployment(KKProjectDeployment deployment)
        {
            if (SelectedProject is null || DeploymentWorkspace.IsSelectedProjectDeploymentActive)
            {
                return;
            }

            DeploymentVersionTag = deployment.Version?.Tag ?? deployment.VersionTag;
            DeploymentDescription =
                deployment.Version?.Description ?? deployment.VersionDescription ?? string.Empty;
            _deploymentEditorProjectId = SelectedProject.Id;
        }

        public async Task<bool> DeploySelectedProjectAsync()
        {
            if (SelectedProject is null ||
                !CanCheckDeployment)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(DeploymentVersionTag))
            {
                PublishNotification(Texts.EnterTagNewVersion, isError: true);
                _versionTagError = VersionTagError.Required;
                return false;
            }

            var projectId = SelectedProject.Id;
            var deployedTag = DeploymentVersionTag.Trim();

            ClearStatusNotification();

            var state = new DeploymentUiState
            {
                ProjectId = projectId,
                VersionTag = deployedTag,
                Description = DeploymentDescription,
                OperationType = DeploymentOperationType.Deploy,
                PreflightRevision = DeploymentWorkspace.GetPreflightRevision(projectId),
                GitHubBranch = IsSelectedProjectGitHub ? SelectedProject.GitHubBranch : null,
                GitHubCommit = IsSelectedProjectGitHub ? DeploymentGitHubSource.Commit : null,
            };

            DeploymentWorkspace.SetState(state);
            DeploymentWorkspace.NotifySelectedDeploymentStateChanged();

            var progress = new UiThreadProgress<DeploymentProgress>(value =>
                DeploymentWorkspace.HandleDeploymentProgress(projectId, state.OperationId, value));
            var preflightProgress = new UiThreadProgress<DeploymentPreflightResult>(value =>
                DeploymentWorkspace.HandleDeploymentPreflight(projectId, state.OperationId, value));

            try
            {
                await _projectDeploymentService.DeployAsync(
                    new DeploymentRequest(
                        projectId,
                        deployedTag,
                        DeploymentDescription,
                        state.GitHubBranch,
                        state.GitHubCommit?.Sha),
                    progress,
                    preflightProgress);

                if (SelectedProject?.Id == projectId)
                {
                    PublishNotification(
                        state.PreflightResult?.HasWarnings == true ?
                            LocalizeFormat(
                                Texts.VersionDeployedPreflightWarnings,
                                deployedTag) :
                            LocalizeFormat(Texts.VersionDeployedSuccessfully, deployedTag),
                        isError: false);
                }

                await ReloadSelectedProjectAsync(projectId);

                return true;
            }
            catch (OperationCanceledException)
            {
                DeploymentWorkspace.AppendDeploymentLog(state, Texts.OperationCancelled);

                if (SelectedProject?.Id == projectId)
                {
                    PublishNotification(Texts.OperationCancelled, isError: false);
                }

                return false;
            }
            catch (Exception exception)
            {
                DeploymentWorkspace.AppendDeploymentLog(state, LocalizeFormat(Texts.ERROR, Localize(exception.Message)));

                if (SelectedProject?.Id == projectId)
                {
                    PublishNotification(Localize(exception.Message), isError: true);
                    if (exception is VersionTagException versionException)
                    {
                        _versionTagError = versionException.Error;
                        _failedVersionTag = deployedTag;
                    }
                }

                await ReloadSelectedProjectAsync(projectId);

                return false;
            }
            finally
            {
                DeploymentWorkspace.CompleteUiStateIfDetached(state);
            }
        }

        public void PrepareDeploymentView()
        {
            if (SelectedProject is null || DeploymentWorkspace.IsSelectedProjectDeploymentActive)
            {
                return;
            }

            DeploymentWorkspace.RemoveState(SelectedProject.Id);
            DeploymentWorkspace.NotifySelectedDeploymentStateChanged();
            if (IsSelectedProjectGitHub)
            {
                _ = RefreshDeploymentCommitAsync();
            }
        }

        public async Task<bool> RollbackAsync(ProjectVersionItemViewModel item)
        {
            if (SelectedProject is null ||
                DeploymentWorkspace.IsSelectedProjectDeploymentActive)
            {
                return false;
            }

            var projectId = SelectedProject.Id;
            var state = new DeploymentUiState
            {
                ProjectId = projectId,
                VersionTag = item.Tag,
                OperationType = DeploymentOperationType.Rollback,
            };

            DeploymentWorkspace.SetState(state);
            DeploymentWorkspace.NotifySelectedDeploymentStateChanged();

            var progress = new UiThreadProgress<DeploymentProgress>(value =>
                DeploymentWorkspace.HandleDeploymentProgress(projectId, state.OperationId, value));

            try
            {
                await _projectDeploymentService.RollbackAsync(
                    projectId,
                    item.Version.Id,
                    progress);

                if (SelectedProject?.Id == projectId)
                {
                    PublishNotification(
                        LocalizeFormat(Texts.RolledBackVersion, item.Tag),
                        isError: false);
                }

                await ReloadSelectedProjectAsync(projectId);

                return true;
            }
            catch (OperationCanceledException)
            {
                DeploymentWorkspace.AppendDeploymentLog(state, Texts.OperationCancelled);

                if (SelectedProject?.Id == projectId)
                {
                    PublishNotification(Texts.OperationCancelled, isError: false);
                }

                return false;
            }
            catch (Exception exception)
            {
                DeploymentWorkspace.AppendDeploymentLog(state, LocalizeFormat(Texts.ERROR, Localize(exception.Message)));

                if (SelectedProject?.Id == projectId)
                {
                    PublishNotification(Localize(exception.Message), isError: true);
                }

                return false;
            }
            finally
            {
                DeploymentWorkspace.CompleteUiStateIfDetached(state);
            }
        }

        public void CancelSelectedDeployment()
        {
            if (DeploymentWorkspace.SelectedDeploymentState is not { QueueStatus: DeploymentQueueStatus.Waiting } state)
            {
                return;
            }

            var itemId = state.QueueItemId ?? _deploymentOperationQueue
                .GetItems()
                .LastOrDefault(item =>
                    item.ProjectId == state.ProjectId &&
                    item.Status == DeploymentQueueStatus.Waiting)
                ?.Id;

            if (itemId.HasValue)
            {
                _deploymentOperationQueue.Cancel(itemId.Value);
            }
        }

        private static async Task<string?> ReadDeploymentLogAsync(string? logPath)
        {
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return null;
            }

            try
            {
                var logsDirectory = Path.GetFullPath(DatabasePaths.LogsDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var resolvedLogPath = Path.GetFullPath(logPath);

                if (!resolvedLogPath.StartsWith(logsDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(resolvedLogPath))
                {
                    return null;
                }

                return await File.ReadAllTextAsync(resolvedLogPath);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                ArgumentException or
                NotSupportedException or
                System.Security.SecurityException)
            {
                return null;
            }
        }

        partial void OnDeploymentVersionTagChanged(string value)
        {
            InvalidateDeploymentPreflight(SelectedProject?.Id);
            if (!IsNotificationError)
            {
                return;
            }

            if ((_versionTagError == VersionTagError.Required && !string.IsNullOrWhiteSpace(value)) ||
                (_versionTagError == VersionTagError.InvalidFormat && IsValidVersionTag(value)) ||
                (_versionTagError == VersionTagError.AlreadyExists &&
                    !string.Equals(value.Trim(), _failedVersionTag, StringComparison.OrdinalIgnoreCase)))
            {
                ClearStatusNotification();
            }
        }

        private static bool IsValidVersionTag(string value)
        {
            var tag = value.Trim();
            if (tag.Length is < 1 or > 200 || !char.IsAsciiLetterOrDigit(tag[0]))
            {
                return false;
            }

            return tag.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
        }
    }
}
