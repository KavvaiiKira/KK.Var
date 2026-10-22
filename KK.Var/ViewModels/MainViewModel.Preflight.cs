using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        private CancellationTokenSource? _preflightCancellation;

        private Guid? _preflightProjectId;

        [ObservableProperty]
        public partial bool IsPreflightChecking { get; set; }

        public bool CanCheckDeployment =>
            SelectedProject is not null &&
            !IsPreflightChecking &&
            !ProjectDetails.IsProjectDetailsLoading &&
            !DeploymentWorkspace.IsSelectedProjectDeploymentActive &&
            !EnvironmentEditor.HasUnsavedEnvironmentChanges &&
            !EnvironmentEditor.IsEnvironmentSaving &&
            (!IsSelectedProjectGitHub || DeploymentGitHubSource.HasCommit);

        public IReadOnlyList<DeploymentPreflightItemViewModel> DeploymentPreflightItems =>
            DeploymentWorkspace.DeploymentPreflightItems;

        public async Task CheckSelectedDeploymentAsync()
        {
            if (!CanCheckDeployment || SelectedProject is null)
            {
                return;
            }

            var projectId = SelectedProject.Id;
            var revision = DeploymentWorkspace.GetPreflightRevision(projectId);
            var state = new DeploymentUiState
            {
                ProjectId = projectId,
                VersionTag = DeploymentVersionTag.Trim(),
                QueueStatus = DeploymentQueueStatus.Completed,
                PreflightRevision = revision,
            };
            DeploymentWorkspace.SetState(state);
            var cancellation = new CancellationTokenSource();
            _preflightCancellation = cancellation;
            _preflightProjectId = projectId;
            IsPreflightChecking = true;
            ClearStatusNotification();
            DeploymentWorkspace.NotifySelectedDeploymentStateChanged();

            try
            {
                var result = await _deploymentPreflightService.CheckAsync(
                    new DeploymentRequest(
                        projectId,
                        state.VersionTag,
                        DeploymentDescription,
                        IsSelectedProjectGitHub ? DeploymentGitHubSource.BranchName : null,
                        IsSelectedProjectGitHub ? DeploymentGitHubSource.Commit?.Sha : null),
                    cancellationToken: cancellation.Token);

                if (revision == DeploymentWorkspace.GetPreflightRevision(projectId) &&
                    DeploymentWorkspace.TryGetDeploymentState(projectId, state.OperationId, out var current))
                {
                    current.PreflightResult = result;
                    DeploymentWorkspace.NotifySelectedDeploymentStateChanged();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (revision == DeploymentWorkspace.GetPreflightRevision(projectId) && SelectedProject?.Id == projectId)
                {
                    state.PreflightResult = new DeploymentPreflightResult(
                        [new DeploymentPreflightCheck(
                            PreflightCheckKeys.Check,
                            PreflightStatus.Error,
                            Localize("Не удалось выполнить проверку."),
                            Localize(exception.Message))]);
                    DeploymentWorkspace.NotifySelectedDeploymentStateChanged();
                }
            }
            finally
            {
                _preflightCancellation = null;
                _preflightProjectId = null;
                cancellation.Dispose();
                IsPreflightChecking = false;
            }
        }

        public void InvalidateDeploymentPreflight(Guid? projectId = null)
        {
            if (projectId is null || projectId == _preflightProjectId)
            {
                _preflightCancellation?.Cancel();
            }

            DeploymentWorkspace.InvalidatePreflight(projectId);

            DeploymentWorkspace.NotifySelectedDeploymentStateChanged();
        }

        partial void OnIsPreflightCheckingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCheckDeployment));
            NotifyOperationStateChanged();
        }
    }
}
