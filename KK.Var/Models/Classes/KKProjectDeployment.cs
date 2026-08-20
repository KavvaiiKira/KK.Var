using System;
using KK.Var.Enums;

namespace KK.Var.Models;

public sealed class KKProjectDeployment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid KKProjectId { get; set; }

    public Guid KKProjectVersionId { get; set; }

    public DeploymentOperationType OperationType { get; set; }

    public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;

    public string RemoteOperationId { get; set; } = string.Empty;

    public DeploymentStage Stage { get; set; } = DeploymentStage.Preparing;

    public DeploymentUnitChange UnitChange { get; set; } = DeploymentUnitChange.Unchanged;

    public string VariablesSnapshotJson { get; set; } = "{}";

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public string? LogPath { get; set; }

    public string? ErrorMessage { get; set; }

    public KKProject Project { get; set; } = null!;

    public KKProjectVersion Version { get; set; } = null!;
}
