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
    IUserSettingsService userSettingsService,
    ILocalizationService localizationService)
    : IKKProjectDeploymentService
{
    private readonly SemaphoreSlim _deploymentLock = new SemaphoreSlim(1, 1);

    public Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
        string? projectName,
        string? searchText,
        DateTime? startedFromUtc,
        DateTime? startedBeforeUtc,
        DeploymentStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        deploymentRepository.SearchAsync(
            projectName,
            searchText,
            startedFromUtc,
            startedBeforeUtc,
            status,
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
        string remoteOperationId,
        string logPath,
        CancellationToken cancellationToken = default)
    {
        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
            throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var version = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
            throw new KeyNotFoundException($"Version '{versionId}' was not found.");

        if (version.KKProjectId != projectId)
        {
            throw new ArgumentException("Version does not belong to the specified project.");
        }

        ValidateJsonObject(variablesSnapshotJson, nameof(variablesSnapshotJson));

        if (!Guid.TryParseExact(remoteOperationId, "N", out _))
        {
            throw new ArgumentException(
                "Invalid remote operation identifier.",
                nameof(remoteOperationId));
        }

        var deployment = new KKProjectDeployment
        {
            Id = Guid.NewGuid(),
            KKProjectId = projectId,
            KKProjectVersionId = versionId,
            OperationType = operationType,
            Status = DeploymentStatus.Running,
            RemoteOperationId = remoteOperationId,
            Stage = DeploymentStage.Preparing,
            UnitChange = DeploymentUnitChange.Unchanged,
            VariablesSnapshotJson = variablesSnapshotJson.Trim(),
            StartedAtUtc = DateTime.UtcNow,
            LogPath = logPath,
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
            DeploymentStatus.Cancelled or
            DeploymentStatus.Interrupted))
        {
            throw new ArgumentException("A terminal deployment status is required.");
        }

        var deployment = await deploymentRepository.GetByIdAsync(
                deploymentId,
                cancellationToken) ??
                throw new KeyNotFoundException($"Deployment '{deploymentId}' was not found.");

        if (deployment.Status is DeploymentStatus.Succeeded or
            DeploymentStatus.Failed or
            DeploymentStatus.Cancelled or
            DeploymentStatus.Interrupted)
        {
            throw new InvalidOperationException("Deployment is already completed.");
        }

        deployment.Status = status;
        deployment.CompletedAtUtc = DateTime.UtcNow;
        deployment.LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath.Trim();
        deployment.ErrorMessage =
            string.IsNullOrWhiteSpace(errorMessage) ?
                null :
                errorMessage.Trim();

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
                    cancellationToken) ??
                    throw new KeyNotFoundException(localizationService.Get("Проект не найден."));

            var settings = await userSettingsService.LoadAsync(cancellationToken);
            var architecture = settings.RemoteMachine.Architecture;

            if (string.IsNullOrWhiteSpace(architecture))
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Сначала проверьте SSH-подключение, чтобы определить архитектуру удалённой машины."));
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
            var project = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Get("Проект не найден."));

            var version = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Get("Версия не найдена."));

            if (version.KKProjectId != projectId)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Версия принадлежит другому проекту."));
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
        var remoteOperationId = Guid.NewGuid().ToString("N");

        var deployment = await StartAsync(
            project.Id,
            version.Id,
            operationType,
            snapshot,
            remoteOperationId,
            logPath,
            cancellationToken);

        var remoteCommitted = false;

        try
        {
            await using var log = new StreamWriter(
                logPath,
                false,
                new System.Text.UTF8Encoding(false));

            await remoteDeploymentService.DeployAsync(
                project,
                artifactPath,
                settings.RemoteMachine,
                remoteOperationId,
                log,
                checkpoint => SaveCheckpointAsync(deployment.Id, checkpoint),
                progress,
                cancellationToken);

            remoteCommitted = true;

            await CompleteAsync(
                deployment.Id,
                DeploymentStatus.Succeeded,
                logPath,
                cancellationToken: CancellationToken.None);

            deployment.Status = DeploymentStatus.Succeeded;
            deployment.CompletedAtUtc = DateTime.UtcNow;
            deployment.LogPath = logPath;
            deployment.Project = project;
            deployment.Version = version;

            return deployment;
        }
        catch (Exception exception)
        {
            if (remoteCommitted)
            {
                throw;
            }

            var status =
                exception is OperationCanceledException ?
                    DeploymentStatus.Cancelled :
                    DeploymentStatus.Failed;

            await CompleteAsync(
                deployment.Id,
                status,
                logPath,
                exception.Message,
                CancellationToken.None);

            throw;
        }
    }

    public async Task<int> RecoverInterruptedAsync(
        CancellationToken cancellationToken = default)
    {
        await _deploymentLock.WaitAsync(cancellationToken);
        try
        {
            var runningDeployments = await deploymentRepository.GetRunningAsync(cancellationToken);
            if (runningDeployments.Count == 0)
            {
                return 0;
            }

            var settings = await userSettingsService.LoadAsync(cancellationToken);
            var recoveredCount = 0;

            foreach (var deployment in runningDeployments)
            {
                var logPath = string.IsNullOrWhiteSpace(deployment.LogPath) ?
                    Path.Combine(
                        DatabasePaths.LogsDirectory,
                        $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{deployment.Id:N}.log") :
                        deployment.LogPath;

                Directory.CreateDirectory(DatabasePaths.LogsDirectory);

                await using var log = new StreamWriter(
                    logPath,
                    true,
                    new System.Text.UTF8Encoding(false));

                var result = await remoteDeploymentService.RecoverAsync(
                    deployment.Project,
                    settings.RemoteMachine,
                    deployment.RemoteOperationId,
                    deployment.Stage,
                    deployment.UnitChange,
                    log,
                    cancellationToken);

                var status = result.Outcome ==
                    DeploymentRecoveryOutcome.NewVersionActive ?
                        DeploymentStatus.Succeeded :
                        DeploymentStatus.Interrupted;

                var message = result.Outcome switch
                {
                    DeploymentRecoveryOutcome.NewVersionActive =>
                        localizationService.Get("Прерванный Deploy завершён: новая версия работает."),
                    DeploymentRecoveryOutcome.PreviousVersionRestored =>
                        localizationService.Get("Прерванный Deploy восстановлен: возвращена предыдущая версия."),
                    _ => localizationService.Get(
                        "Прерванный Deploy остановлен до переключения версии."),
                };

                await CompleteAsync(
                    deployment.Id,
                    status,
                    logPath,
                    status == DeploymentStatus.Interrupted ? message : null,
                    cancellationToken);

                recoveredCount++;
            }

            return recoveredCount;
        }
        finally
        {
            _deploymentLock.Release();
        }
    }

    private async Task SaveCheckpointAsync(
        Guid deploymentId,
        DeploymentCheckpoint checkpoint)
    {
        var deployment = await deploymentRepository.GetByIdAsync(
                deploymentId,
                CancellationToken.None) ??
                throw new KeyNotFoundException($"Deployment '{deploymentId}' was not found.");

        if (deployment.Status != DeploymentStatus.Running)
        {
            throw new InvalidOperationException("Deployment is no longer running.");
        }

        deployment.Stage = checkpoint.Stage;
        deployment.UnitChange = checkpoint.UnitChange;

        await deploymentRepository.UpdateAsync(deployment, CancellationToken.None);
    }

    private string ResolveArtifactPath(string relativePath)
    {
        var root = Path.GetFullPath(DatabasePaths.ArtifactsDirectory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(localizationService.Get("Путь к архиву версии недопустим."));
        }

        return path;
    }

    private async Task VerifyArtifactAsync(
        string path,
        KKProjectVersion version,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                localizationService.Get(
                    "Локальный архив выбранной версии не найден."),
                path);
        }

        await using var stream = File.OpenRead(path);

        var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));

        if (!string.Equals(hash, version.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(localizationService.Get("Контрольная сумма архива версии не совпадает."));
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
