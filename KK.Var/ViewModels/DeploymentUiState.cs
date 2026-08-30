using System;
using KK.Var.Enums;

namespace KK.Var.ViewModels;

internal sealed class DeploymentUiState
{
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

    public bool IsActive =>
        QueueStatus is DeploymentQueueStatus.Waiting or DeploymentQueueStatus.Running;
}
