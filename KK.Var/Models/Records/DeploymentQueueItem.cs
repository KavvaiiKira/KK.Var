using System;
using KK.Var.Enums;

namespace KK.Var.Models
{
    public sealed record DeploymentQueueItem
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        public Guid ProjectId { get; init; }

        public Guid? RemoteMachineId { get; init; }

        public string Version { get; init; } = string.Empty;

        public DeploymentOperationType OperationType { get; init; }

        public DateTime AddedAtUtc { get; init; } = DateTime.UtcNow;

        public DeploymentQueueStatus Status { get; init; } =
            DeploymentQueueStatus.Waiting;
    }
}
