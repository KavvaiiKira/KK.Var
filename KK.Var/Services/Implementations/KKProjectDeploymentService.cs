using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectDeploymentService(
    IKKProjectRepository projectRepository,
    IKKProjectVersionRepository versionRepository,
    IKKProjectDeploymentRepository deploymentRepository)
    : IKKProjectDeploymentService
{
    public Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
        string? projectName,
        string? searchText,
        DateTime? startedFromUtc,
        DateTime? startedBeforeUtc,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        deploymentRepository.SearchAsync(
            projectName,
            searchText,
            startedFromUtc,
            startedBeforeUtc,
            skip,
            take,
            cancellationToken);

    public Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        deploymentRepository.GetByProjectIdAsync(projectId, cancellationToken);

    public async Task<KKProjectDeployment> StartAsync(
        Guid projectId,
        Guid versionId,
        DeploymentOperationType operationType,
        string variablesSnapshotJson,
        CancellationToken cancellationToken = default)
    {
        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var version = await versionRepository.GetByIdAsync(versionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Version '{versionId}' was not found.");

        if (version.KKProjectId != projectId)
        {
            throw new ArgumentException("Version does not belong to the specified project.");
        }

        ValidateJsonObject(variablesSnapshotJson, nameof(variablesSnapshotJson));

        var deployment = new KKProjectDeployment
        {
            Id = Guid.NewGuid(),
            KKProjectId = projectId,
            KKProjectVersionId = versionId,
            OperationType = operationType,
            Status = DeploymentStatus.Running,
            VariablesSnapshotJson = variablesSnapshotJson.Trim(),
            StartedAtUtc = DateTime.UtcNow,
        };

        await deploymentRepository.AddAsync(deployment, cancellationToken);
        return deployment;
    }

    public async Task CompleteAsync(
        Guid deploymentId,
        DeploymentStatus status,
        string? logPath = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (status is not (DeploymentStatus.Succeeded or
            DeploymentStatus.Failed or
            DeploymentStatus.Cancelled))
        {
            throw new ArgumentException("A terminal deployment status is required.");
        }

        var deployment = await deploymentRepository.GetByIdAsync(
                deploymentId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Deployment '{deploymentId}' was not found.");

        if (deployment.Status is DeploymentStatus.Succeeded or
            DeploymentStatus.Failed or
            DeploymentStatus.Cancelled)
        {
            throw new InvalidOperationException("Deployment is already completed.");
        }

        deployment.Status = status;
        deployment.CompletedAtUtc = DateTime.UtcNow;
        deployment.LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath.Trim();
        deployment.ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? null
            : errorMessage.Trim();

        await deploymentRepository.UpdateAsync(deployment, cancellationToken);
    }

    private static void ValidateJsonObject(string json, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON object is required.", parameterName);
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("JSON object is required.", parameterName);
        }
    }
}
