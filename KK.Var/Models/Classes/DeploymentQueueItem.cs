using System;
using System.Threading;
using KK.Var.Enums;

namespace KK.Var.Models;

public sealed class DeploymentQueueItem
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; init; }

    public string Version { get; init; } = string.Empty;

    public DeploymentOperationType OperationType { get; init; }

    public DateTime AddedAtUtc { get; init; } = DateTime.UtcNow;

    public CancellationTokenSource CancellationTokenSource { get; init; } =
        new CancellationTokenSource();

    public DeploymentQueueStatus Status { get; internal set; } =
        DeploymentQueueStatus.Waiting;
}
