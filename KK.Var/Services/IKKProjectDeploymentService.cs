using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IKKProjectDeploymentService
{
    Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<KKProjectDeployment> StartAsync(
        Guid projectId,
        Guid versionId,
        DeploymentOperationType operationType,
        string variablesSnapshotJson,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        Guid deploymentId,
        DeploymentStatus status,
        string? logPath = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
}
