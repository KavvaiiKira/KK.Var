using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels;

public partial class MainViewModel
{
    private CancellationTokenSource? _preflightCancellation;
    private Guid? _preflightProjectId;
    private readonly Dictionary<Guid, int> _preflightRevisions = new Dictionary<Guid, int>();

    [ObservableProperty]
    public partial bool IsPreflightChecking { get; set; }

    public bool CanCheckDeployment =>
        SelectedProject is not null &&
        !IsPreflightChecking &&
        !IsProjectDetailsLoading &&
        !IsSelectedProjectDeploymentActive &&
        !HasUnsavedEnvironmentChanges &&
        !IsEnvironmentSaving;

    public IReadOnlyList<DeploymentPreflightItemViewModel> DeploymentPreflightItems =>
        SelectedDeploymentState?.PreflightItems ?? [];

    public async Task CheckSelectedDeploymentAsync()
    {
        if (_deploymentPreflightService is null || !CanCheckDeployment || SelectedProject is null)
        {
            return;
        }

        var projectId = SelectedProject.Id;
        var revision = GetPreflightRevision(projectId);
        var state = new DeploymentUiState
        {
            ProjectId = projectId,
            VersionTag = DeploymentVersionTag.Trim(),
            QueueStatus = DeploymentQueueStatus.Completed,
            PreflightRevision = revision,
        };
        _deploymentStates[projectId] = state;
        var cancellation = new CancellationTokenSource();
        _preflightCancellation = cancellation;
        _preflightProjectId = projectId;
        IsPreflightChecking = true;
        ClearStatusNotification();
        NotifySelectedDeploymentStateChanged();

        try
        {
            var result = await _deploymentPreflightService.CheckAsync(
                new DeploymentRequest(projectId, state.VersionTag, DeploymentDescription),
                cancellationToken: cancellation.Token);

            if (revision == GetPreflightRevision(projectId) &&
                TryGetDeploymentState(projectId, state.OperationId, out var current))
            {
                current.PreflightResult = result;
                NotifySelectedDeploymentStateChanged();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (revision == GetPreflightRevision(projectId) && SelectedProject?.Id == projectId)
            {
                state.PreflightResult = new DeploymentPreflightResult(
                    [new DeploymentPreflightCheck("check", PreflightStatus.Error,
                        Localize("Не удалось выполнить проверку."), exception.Message)]);
                NotifySelectedDeploymentStateChanged();
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

        var projectIds = projectId.HasValue ?
            [projectId.Value] :
            _deploymentStates.Keys.ToArray();
        foreach (var id in projectIds)
        {
            _preflightRevisions[id] = GetPreflightRevision(id) + 1;
            if (_deploymentStates.TryGetValue(id, out var state))
            {
                state.PreflightResult = null;
            }
        }

        NotifySelectedDeploymentStateChanged();
    }

    private int GetPreflightRevision(Guid projectId) =>
        _preflightRevisions.GetValueOrDefault(projectId);

    partial void OnIsPreflightCheckingChanged(bool value) =>
        OnPropertyChanged(nameof(CanCheckDeployment));

    partial void OnHasUnsavedEnvironmentChangesChanged(bool value) =>
        OnPropertyChanged(nameof(CanCheckDeployment));
}
