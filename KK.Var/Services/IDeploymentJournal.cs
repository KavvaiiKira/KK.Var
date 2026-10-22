using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IDeploymentJournal
    {
        Task<KKProjectDeployment> StartAsync(
            Guid projectId,
            Guid versionId,
            DeploymentOperationType operationType,
            DeploymentTargetSnapshot target,
            string variablesSnapshotJson,
            string remoteOperationId,
            string logPath,
            CancellationToken cancellationToken = default);

        Task CompleteAsync(
            Guid deploymentId,
            DeploymentStatus status,
            string? logPath = null,
            string? errorMessage = null,
            CancellationToken cancellationToken = default,
            bool automaticRollbackCompleted = false,
            bool automaticRollbackFailed = false);

        Task SaveCheckpointAsync(
            Guid deploymentId,
            DeploymentCheckpoint checkpoint);
    }
}
