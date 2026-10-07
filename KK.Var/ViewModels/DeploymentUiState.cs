using System;
using System.Collections.Generic;
using System.Linq;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels;

internal sealed class DeploymentUiState
{
    private DeploymentPreflightResult? _preflightResult;

    public Guid OperationId { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; init; }

    public Guid? QueueItemId { get; set; }

    public string VersionTag { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DeploymentOperationType OperationType { get; init; }

    public DeploymentQueueStatus QueueStatus { get; set; } =
        DeploymentQueueStatus.Waiting;

    public int ProgressPercentage { get; set; }

    public string ProgressMessage { get; set; } = string.Empty;

    public string LogText { get; set; } = string.Empty;

    public DeploymentPreflightResult? PreflightResult
    {
        get => _preflightResult;
        set
        {
            _preflightResult = value;
            PreflightItems = value?.Checks
                .Select(check => new DeploymentPreflightItemViewModel(check)).ToArray() ?? [];
        }
    }

    public IReadOnlyList<DeploymentPreflightItemViewModel> PreflightItems { get; private set; } = [];

    public int PreflightRevision { get; init; }

    public bool IsActive =>
        QueueStatus is DeploymentQueueStatus.Waiting or DeploymentQueueStatus.Running;
}
