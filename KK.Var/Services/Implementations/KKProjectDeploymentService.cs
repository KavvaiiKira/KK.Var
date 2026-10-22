using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations
{
    public sealed class KKProjectDeploymentService(
        IKKProjectRepository projectRepository,
        IKKProjectVersionRepository versionRepository,
        IDeploymentNotificationService notifications,
        IKKProjectVersionService versionService,
        IProjectArtifactService artifactService,
        IArtifactStorageService artifactStorageService,
        IArtifactStorageActivity storageActivity,
        IDeploymentPreflightService preflightService,
        IVersionCleanupService versionCleanupService,
        IDeploymentExecutor executor,
        IDeploymentOperationQueue operationQueue,
        IRemoteMachineService machineService,
        IRemoteConnectionService remoteConnectionService,
        ILocalizationService localizationService)
        : IKKProjectDeploymentService
    {
        public async Task<KKProjectDeployment> DeployAsync(
            DeploymentRequest request,
            IProgress<DeploymentProgress>? progress = null,
            IProgress<DeploymentPreflightResult>? preflightProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (storageActivity.IsMigrationRunning ||
                storageActivity.IsCleanupRunning)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Нельзя начать Deploy во время переноса локальных версий."));
            }

            var queuedProject = await projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Get("Проект не найден."));
            request = request with { RemoteMachineId = queuedProject.RemoteMachineId };
            await using var machineReservation = await machineService.ReserveAsync(
                queuedProject.RemoteMachineId,
                cancellationToken);
            return await operationQueue.EnqueueAsync(
                request.ProjectId,
                request.VersionTag,
                DeploymentOperationType.Deploy,
                token => notifications.WithFailureNotificationAsync(
                    request.ProjectId,
                    request.VersionTag,
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
                var message = localizationService.Format(
                    "Предварительная проверка остановила Deploy: {0}",
                    firstError.Message);
                if (firstError.VersionError is { } versionError)
                {
                    throw new VersionTagException(versionError, message);
                }
                throw new InvalidOperationException(message);
            }

            var project = operationProject;
            var versionTag = request.VersionTag.Trim();

            if (await versionRepository.TagExistsAsync(
                project.Id,
                versionTag,
                cancellationToken))
            {
                throw new VersionTagException(VersionTagError.AlreadyExists, localizationService.Format(
                    "Версия «{0}» уже существует.",
                    versionTag));
            }

            project.RemoteMachineId = request.RemoteMachineId ?? project.RemoteMachineId;
            var connection = await machineService.GetConnectionAsync(project.RemoteMachineId, cancellationToken);
            var architecture = connection.Architecture;

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

            var deployment = await executor.ExecuteAsync(
                project,
                version,
                artifact.AbsolutePath,
                DeploymentOperationType.Deploy,
                connection,
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
            if (storageActivity.IsMigrationRunning ||
                storageActivity.IsCleanupRunning)
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
            await using var machineReservation = await machineService.ReserveAsync(
                queuedProject.RemoteMachineId,
                cancellationToken);
            return await operationQueue.EnqueueAsync(
                projectId,
                queuedVersion.Tag,
                DeploymentOperationType.Rollback,
                token => notifications.WithFailureNotificationAsync(
        projectId,
        queuedVersion.Tag,
        DeploymentOperationType.Rollback,
        () => RollbackCoreAsync(
            projectId,
            versionId,
            queuedProject.RemoteMachineId,
            progress,
            token,
            queuedProject)),
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
            var remoteChecks = await remoteConnectionService.CheckDeploymentAsync(
                connection,
                project.RemoteDeploymentDirectory,
                Math.Max(64L * 1024 * 1024, version.ArtifactSize * 3),
                project.BuildProvider == ProjectBuildProvider.Python,
                cancellationToken);
            if (remoteChecks.Any(check => check.Status == PreflightStatus.Error))
            {
                throw new InvalidOperationException(remoteChecks.First(check => check.Status == PreflightStatus.Error).Message);
            }

            return await executor.ExecuteAsync(
                project,
                version,
                artifactPath,
                DeploymentOperationType.Rollback,
                connection,
                progress,
                cancellationToken);
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

            var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(
                stream,
                cancellationToken));

            if (!string.Equals(hash, version.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(localizationService.Get("Контрольная сумма архива версии не совпадает."));
            }
        }
    }
}
