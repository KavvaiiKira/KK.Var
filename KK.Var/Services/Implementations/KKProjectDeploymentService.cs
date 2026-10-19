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
    IArtifactStorageService artifactStorageService,
    IDeploymentPreflightService preflightService,
    IVersionCleanupService versionCleanupService,
    IRemoteDeploymentService remoteDeploymentService,
    IDeploymentOperationQueue operationQueue,
    IRemoteMachineService machineService,
    IRemoteConnectionService remoteConnectionService,
    ILocalizationService localizationService)
    : IKKProjectDeploymentService
{
    public event Action<DeploymentCompletionNotice>? DeploymentCompleted;

    public Task<KKProjectDeployment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        deploymentRepository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
        string? projectName,
        string? searchText,
        DateTime? startedFromUtc,
        DateTime? startedBeforeUtc,
        DeploymentStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default,
        Guid? remoteMachineId = null) =>
        deploymentRepository.SearchAsync(
            projectName,
            searchText,
            startedFromUtc,
            startedBeforeUtc,
            status,
            skip,
            take,
            cancellationToken,
            remoteMachineId);

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
        CancellationToken cancellationToken = default,
        Guid? remoteMachineId = null)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
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
            RemoteMachineId = remoteMachineId ?? project.RemoteMachineId,
            KKProjectVersionId = versionId,
            VersionTag = version.Tag,
            VersionDescription = version.Description,
            SourceCommitSha = version.SourceCommitSha,
            OperationType = operationType,
            Status = DeploymentStatus.Running,
            RemoteOperationId = remoteOperationId,
            Stage = DeploymentStage.Preparing,
            UnitChange = DeploymentUnitChange.Unchanged,
            VariablesSnapshotJson = variablesSnapshotJson.Trim(),
            StartedAtUtc = DateTime.UtcNow,
            LogPath = logPath,
        };

        var machine = await machineService.GetAsync(deployment.RemoteMachineId, cancellationToken);
        deployment.RemoteMachineName = machine.Name;
        deployment.RemoteHost = machine.Host;
        deployment.RemotePort = machine.Port;
        deployment.RemoteUserName = machine.UserName;
        deployment.RemoteArchitecture = machine.Architecture ?? string.Empty;
        deployment.RemoteServerGeneration = machine.ServerGeneration;
        deployment.RemoteServiceNameSnapshot = project.RemoteServiceName;
        deployment.RemoteDirectorySnapshot = project.RemoteDeploymentDirectory;
        deployment.RemoteExecutableSnapshot = project.RemoteExecutableFileName;
        await deploymentRepository.AddAsync(deployment, cancellationToken);

        return deployment;
    }

    public async Task CompleteAsync(
        Guid deploymentId,
        DeploymentStatus status,
        string? logPath = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default,
        bool automaticRollbackCompleted = false,
        bool automaticRollbackFailed = false)
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
        if (status is DeploymentStatus.Succeeded or DeploymentStatus.Failed || automaticRollbackCompleted)
        {
            await ReportPersistedCompletionAsync(deployment, automaticRollbackCompleted, automaticRollbackFailed);
        }
    }

    public async Task<KKProjectDeployment> DeployAsync(
        DeploymentRequest request,
        IProgress<DeploymentProgress>? progress = null,
        IProgress<DeploymentPreflightResult>? preflightProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (artifactStorageService.IsMigrationRunning ||
            artifactStorageService.IsCleanupRunning)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя начать Deploy во время переноса локальных версий."));
        }

        var queuedProject = await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) ??
            throw new KeyNotFoundException(localizationService.Get("Проект не найден."));
        request = request with { RemoteMachineId = queuedProject.RemoteMachineId };
        await using var machineReservation = await machineService.ReserveAsync(queuedProject.RemoteMachineId, cancellationToken);
        return await operationQueue.EnqueueAsync(
            request.ProjectId,
            request.VersionTag,
            DeploymentOperationType.Deploy,
            token => WithFailureNotificationAsync(request.ProjectId, request.VersionTag,
                DeploymentOperationType.Deploy,
                () => DeployCoreAsync(request, progress, preflightProgress, token, queuedProject)),
            cancellationToken,
            remoteMachineId: queuedProject.RemoteMachineId);
    }

    private async Task<KKProjectDeployment> DeployCoreAsync(
        DeploymentRequest request,
        IProgress<DeploymentProgress>? progress,
        IProgress<DeploymentPreflightResult>? preflightProgress,
        CancellationToken cancellationToken,
        KKProject operationProject)
    {
        progress?.Report(new DeploymentProgress(
            1,
            localizationService.Get("Предварительная проверка Deploy")));
        var preflight = await preflightService.CheckAsync(
            request,
            currentOperationOwnsQueue: true,
            cancellationToken,
            operationProject);
        preflightProgress?.Report(preflight);
        if (preflight.FirstError is { } firstError)
        {
            throw new InvalidOperationException(localizationService.Format(
                "Предварительная проверка остановила Deploy: {0}",
                firstError.Message));
        }

        var project = operationProject;
        var versionTag = request.VersionTag.Trim();

        if (await versionRepository.TagExistsAsync(
                project.Id,
                versionTag,
                cancellationToken))
        {
            throw new InvalidOperationException(localizationService.Format(
                "Версия «{0}» уже существует.",
                versionTag));
        }

        project.RemoteMachineId = request.RemoteMachineId ?? project.RemoteMachineId;
        var settings = new UserSettings
        {
            RemoteMachine = await machineService.GetConnectionAsync(project.RemoteMachineId, cancellationToken),
        };
        var architecture = settings.RemoteMachine.Architecture;

        if (string.IsNullOrWhiteSpace(architecture))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Сначала проверьте SSH-подключение, чтобы определить архитектуру удалённой машины."));
        }

        var artifact = await artifactService.CreateAsync(
            project,
            versionTag,
            architecture,
            progress,
            preflight.SourceCommitSha,
            cancellationToken);

        var version = await versionService.CreateAsync(
            new KKProjectVersion
            {
                KKProjectId = project.Id,
                Tag = versionTag,
                Description = request.Description,
                ArtifactRelativePath = artifact.RelativePath,
                ArtifactSha256 = artifact.Sha256,
                ArtifactSize = artifact.Size,
                SourceCommitSha = artifact.SourceCommitSha,
                TargetArchitecture = architecture!,
            },
            cancellationToken);

        var deployment = await ExecuteRemoteAsync(
            project,
            version,
            artifact.AbsolutePath,
            DeploymentOperationType.Deploy,
            settings,
            progress,
            cancellationToken);

        try
        {
            await versionCleanupService.ExecuteAsync(
                project.Id,
                afterSuccessfulDeploy: true,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(localizationService.Format(
                "Deploy завершён, но очистка версий не удалась: {0}",
                exception.Message), exception);
        }

        return deployment;
    }

    public async Task<KKProjectDeployment> RollbackAsync(
        Guid projectId,
        Guid versionId,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (artifactStorageService.IsMigrationRunning ||
            artifactStorageService.IsCleanupRunning)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя начать rollback во время переноса локальных версий."));
        }

        var queuedVersion = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
            throw new KeyNotFoundException(localizationService.Get("Версия не найдена."));

        if (queuedVersion.KKProjectId != projectId)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Версия принадлежит другому проекту."));
        }

        var queuedProject = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
            throw new KeyNotFoundException(localizationService.Get("Проект не найден."));
        await using var machineReservation = await machineService.ReserveAsync(queuedProject.RemoteMachineId, cancellationToken);
        return await operationQueue.EnqueueAsync(
            projectId,
            queuedVersion.Tag,
            DeploymentOperationType.Rollback,
            token => WithFailureNotificationAsync(projectId, queuedVersion.Tag,
                DeploymentOperationType.Rollback,
                () => RollbackCoreAsync(projectId, versionId, queuedProject.RemoteMachineId, progress, token, queuedProject)),
            cancellationToken,
            remoteMachineId: queuedProject.RemoteMachineId);
    }

    private async Task<KKProjectDeployment> RollbackCoreAsync(
        Guid projectId,
        Guid versionId,
        Guid machineId,
        IProgress<DeploymentProgress>? progress,
        CancellationToken cancellationToken,
        KKProject operationProject)
    {
        var project = operationProject;

        var version = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
            throw new KeyNotFoundException(localizationService.Get("Версия не найдена."));

        if (version.KKProjectId != projectId)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Версия принадлежит другому проекту."));
        }

        var artifactPath = await artifactStorageService.ResolvePathAsync(
            version.ArtifactRelativePath,
            cancellationToken);

        await VerifyArtifactAsync(artifactPath, version, cancellationToken);

        project.RemoteMachineId = machineId;
        var connection = await machineService.GetConnectionAsync(machineId, cancellationToken);
        if (!ArchitectureCompatibility.IsCompatible(version.TargetArchitecture, connection.Architecture))
        {
            throw new InvalidOperationException(localizationService.Get("Архитектура архива несовместима с выбранной машиной."));
        }
        var settings = new UserSettings { RemoteMachine = connection };
        var remoteChecks = await remoteConnectionService.CheckDeploymentAsync(connection,
            project.RemoteDeploymentDirectory, Math.Max(64L * 1024 * 1024, version.ArtifactSize * 3),
            project.BuildProvider == ProjectBuildProvider.Python, cancellationToken);
        if (remoteChecks.Any(check => check.Status == PreflightStatus.Error))
        {
            throw new InvalidOperationException(remoteChecks.First(check => check.Status == PreflightStatus.Error).Message);
        }

        return await ExecuteRemoteAsync(
            project,
            version,
            artifactPath,
            DeploymentOperationType.Rollback,
            settings,
            progress,
            cancellationToken);
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
        if (!ArchitectureCompatibility.IsCompatible(version.TargetArchitecture, settings.RemoteMachine.Architecture))
        {
            throw new InvalidOperationException(localizationService.Get("Архитектура архива несовместима с выбранной машиной."));
        }
        var machine = await machineService.GetAsync(project.RemoteMachineId, cancellationToken);
        settings.RemoteMachine.CleanDeployment = machine.ServerGeneration > 0 &&
            !(await deploymentRepository.GetByProjectIdAsync(project.Id, cancellationToken)).Any(item =>
                item.RemoteMachineId == machine.Id && item.RemoteServerGeneration == machine.ServerGeneration &&
                item.Status == DeploymentStatus.Succeeded);
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
            cancellationToken,
            remoteMachineId: project.RemoteMachineId);

        deployment.RemoteServiceNameSnapshot = project.RemoteServiceName;
        deployment.RemoteDirectorySnapshot = project.RemoteDeploymentDirectory;
        deployment.RemoteExecutableSnapshot = project.RemoteExecutableFileName;
        await deploymentRepository.UpdateAsync(deployment, cancellationToken);

        var remoteCommitted = false;

        try
        {
            await using var log = new StreamWriter(
                logPath,
                false,
                new System.Text.UTF8Encoding(false))
            {
                AutoFlush = true,
            };

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
                CancellationToken.None,
                exception is RemoteDeploymentFailureException { AutomaticRollbackCompleted: true },
                exception is RemoteDeploymentFailureException { AutomaticRollbackCompleted: false });

            throw;
        }
    }

    public async Task<int> RecoverInterruptedAsync(
        CancellationToken cancellationToken = default)
    {
        var runningDeployments = await deploymentRepository.GetRunningAsync(cancellationToken);
        if (runningDeployments.Count == 0)
        {
            return 0;
        }

        var recoveredCount = 0;

        foreach (var deployment in runningDeployments)
        {
            await using var machineReservation = await machineService.ReserveAsync(deployment.RemoteMachineId, cancellationToken);
            var machine = await machineService.GetAsync(deployment.RemoteMachineId, cancellationToken);
            if (machine.ServerGeneration != deployment.RemoteServerGeneration)
            {
                throw new InvalidOperationException(localizationService.Get("Состояние прежнего сервера неизвестно. Recovery остановлен."));
            }
            var settings = new UserSettings
            {
                RemoteMachine = await machineService.GetConnectionAsync(deployment.RemoteMachineId, cancellationToken),
            };
            deployment.Project = await projectRepository.GetByIdAsync(deployment.KKProjectId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Get("Проект не найден."));
            if (!string.IsNullOrWhiteSpace(deployment.RemoteDirectorySnapshot))
            {
                deployment.Project.RemoteDeploymentDirectory = deployment.RemoteDirectorySnapshot;
                deployment.Project.RemoteServiceName = deployment.RemoteServiceNameSnapshot;
                deployment.Project.RemoteExecutableFileName = deployment.RemoteExecutableSnapshot;
            }
            recoveredCount += await operationQueue.EnqueueAsync(
                deployment.KKProjectId,
                deployment.VersionTag,
                deployment.OperationType,
                token => RecoverInterruptedCoreAsync(deployment, settings, token),
                cancellationToken,
                remoteMachineId: deployment.RemoteMachineId);
        }

        return recoveredCount;
    }

    private async Task<int> RecoverInterruptedCoreAsync(
        KKProjectDeployment deployment,
        UserSettings settings,
        CancellationToken cancellationToken)
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
            new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true,
        };

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
            cancellationToken,
            automaticRollbackCompleted: result.Outcome == DeploymentRecoveryOutcome.PreviousVersionRestored);

        return 1;
    }

    private async Task ReportPersistedCompletionAsync(KKProjectDeployment deployment,
        bool automaticRollbackCompleted = false, bool automaticRollbackFailed = false)
    {
        try
        {
            var project = await projectRepository.GetByIdAsync(deployment.KKProjectId, CancellationToken.None);
            ReportCompletion(new DeploymentCompletionNotice(deployment.KKProjectId, deployment.Id,
                project?.Name ?? string.Empty, deployment.Status, deployment.OperationType,
                automaticRollbackCompleted, automaticRollbackFailed));
        }
        catch
        {
        }
    }

    private void ReportCompletion(DeploymentCompletionNotice notice)
    {
        foreach (var handler in DeploymentCompleted?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<DeploymentCompletionNotice>)handler)(notice);
            }
            catch
            {
            }
        }
    }

    private async Task<KKProjectDeployment> WithFailureNotificationAsync(Guid projectId, string tag,
        DeploymentOperationType operationType, Func<Task<KKProjectDeployment>> operation)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            return await operation();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                var deployments = await deploymentRepository.GetByProjectIdAsync(projectId, CancellationToken.None);
                var deployment = deployments.FirstOrDefault(item =>
                    item.VersionTag == tag.Trim() && item.StartedAtUtc >= startedAt);
                if (deployment is null || deployment.Status is DeploymentStatus.Pending or DeploymentStatus.Running)
                {
                    var project = await projectRepository.GetByIdAsync(projectId, CancellationToken.None);
                    ReportCompletion(new DeploymentCompletionNotice(projectId, deployment?.Id,
                        project?.Name ?? string.Empty, DeploymentStatus.Failed, operationType));
                }
            }
            catch
            {
            }
            throw;
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
