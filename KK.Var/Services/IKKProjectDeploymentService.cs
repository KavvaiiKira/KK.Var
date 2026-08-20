using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IKKProjectDeploymentService
{
    Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
        string? projectName,
        string? searchText,
        DateTime? startedFromUtc,
        DateTime? startedBeforeUtc,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

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

    Task<KKProjectDeployment> DeployAsync(
        DeploymentRequest request,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<KKProjectDeployment> RollbackAsync(
        Guid projectId,
        Guid versionId,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
