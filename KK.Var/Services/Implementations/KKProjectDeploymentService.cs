using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;
using KK.Var.Data;
using KK.Var.Configuration;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectDeploymentService(
    IKKProjectRepository projectRepository,
    IKKProjectVersionRepository versionRepository,
    IKKProjectDeploymentRepository deploymentRepository,
    IKKProjectVersionService versionService,
    IKKProjectEnvironmentService environmentService,
    IProjectArtifactService artifactService,
    IRemoteDeploymentService remoteDeploymentService,
    IUserSettingsService userSettingsService)
    : IKKProjectDeploymentService
{
    private readonly SemaphoreSlim _deploymentLock = new(1, 1);

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

    public async Task<KKProjectDeployment> DeployAsync(
        DeploymentRequest request,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _deploymentLock.WaitAsync(cancellationToken);
        try
        {
            var project = await projectRepository.GetByIdAsync(
                    request.ProjectId,
                    cancellationToken)
                ?? throw new KeyNotFoundException("Проект не найден.");
            var settings = await userSettingsService.LoadAsync(cancellationToken);
            var architecture = settings.RemoteMachine.Architecture;
            if (string.IsNullOrWhiteSpace(architecture))
            {
                throw new InvalidOperationException(
                    "Сначала проверьте SSH-подключение, чтобы определить архитектуру удалённой машины.");
            }

            var artifact = await artifactService.CreateAsync(
                project,
                request.VersionTag,
                architecture,
                progress,
                cancellationToken);
            var version = await versionService.CreateAsync(
                new KKProjectVersion
                {
                    KKProjectId = project.Id,
                    Tag = request.VersionTag,
                    Description = request.Description,
                    ArtifactRelativePath = artifact.RelativePath,
                    ArtifactSha256 = artifact.Sha256,
                    ArtifactSize = artifact.Size,
                    SourceCommitSha = artifact.SourceCommitSha,
                },
                cancellationToken);

            return await ExecuteRemoteAsync(
                project,
                version,
                artifact.AbsolutePath,
                DeploymentOperationType.Deploy,
                settings,
                progress,
                cancellationToken);
        }
        finally
        {
            _deploymentLock.Release();
        }
    }

    public async Task<KKProjectDeployment> RollbackAsync(
        Guid projectId,
        Guid versionId,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _deploymentLock.WaitAsync(cancellationToken);
        try
        {
            var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
                ?? throw new KeyNotFoundException("Проект не найден.");
            var version = await versionRepository.GetByIdAsync(versionId, cancellationToken)
                ?? throw new KeyNotFoundException("Версия не найдена.");
            if (version.KKProjectId != projectId)
            {
                throw new InvalidOperationException("Версия принадлежит другому проекту.");
            }

            var artifactPath = ResolveArtifactPath(version.ArtifactRelativePath);
            await VerifyArtifactAsync(artifactPath, version, cancellationToken);
            var settings = await userSettingsService.LoadAsync(cancellationToken);
            return await ExecuteRemoteAsync(
                project,
                version,
                artifactPath,
                DeploymentOperationType.Rollback,
                settings,
                progress,
                cancellationToken);
        }
        finally
        {
            _deploymentLock.Release();
        }
    }

    private async Task<KKProjectDeployment> ExecuteRemoteAsync(
        KKProject project,
        KKProjectVersion version,
        string artifactPath,
        DeploymentOperationType operationType,
        UserSettings settings,
        IProgress<DeploymentProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DatabasePaths.LogsDirectory);
        var logPath = Path.Combine(
            DatabasePaths.LogsDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        var variables = await environmentService.GetAsync(project.Id, cancellationToken);
        var snapshot = JsonSerializer.Serialize(variables.ToDictionary(pair => pair.Key, pair => pair.Value));
        var deployment = await StartAsync(
            project.Id,
            version.Id,
            operationType,
            snapshot,
            cancellationToken);

        try
        {
            await using var log = new StreamWriter(
                logPath,
                false,
                new System.Text.UTF8Encoding(false));
            var visibleLog = new DeploymentProgressTextWriter(log, progress);
            await remoteDeploymentService.DeployAsync(
                project,
                artifactPath,
                settings.RemoteMachine,
                visibleLog,
                progress,
                cancellationToken);
            await CompleteAsync(
                deployment.Id,
                DeploymentStatus.Succeeded,
                logPath,
                cancellationToken: cancellationToken);
            deployment.Status = DeploymentStatus.Succeeded;
            deployment.CompletedAtUtc = DateTime.UtcNow;
            deployment.LogPath = logPath;
            deployment.Project = project;
            deployment.Version = version;
            return deployment;
        }
        catch (Exception exception)
        {
            var status = exception is OperationCanceledException
                ? DeploymentStatus.Cancelled
                : DeploymentStatus.Failed;
            await CompleteAsync(
                deployment.Id,
                status,
                logPath,
                exception.Message,
                CancellationToken.None);
            throw;
        }
    }

    private static string ResolveArtifactPath(string relativePath)
    {
        var root = Path.GetFullPath(DatabasePaths.ArtifactsDirectory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Путь к архиву версии недопустим.");
        }
        return path;
    }

    private static async Task VerifyArtifactAsync(
        string path,
        KKProjectVersion version,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Локальный архив выбранной версии не найден.", path);
        }
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(
            await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, version.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Контрольная сумма архива версии не совпадает.");
        }
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
