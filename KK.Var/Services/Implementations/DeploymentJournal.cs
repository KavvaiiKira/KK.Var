using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations
{
    public sealed class DeploymentJournal(
        IKKProjectRepository projectRepository,
        IKKProjectVersionRepository versionRepository,
        IKKProjectDeploymentRepository deploymentRepository,
        IRemoteMachineService machineService,
        IDeploymentNotificationService notifications,
        ILocalizationService localizationService) : IDeploymentJournal
    {
        public async Task<KKProjectDeployment> StartAsync(
            Guid projectId,
            Guid versionId,
            DeploymentOperationType operationType,
            DeploymentTargetSnapshot target,
            string variablesSnapshotJson,
            string remoteOperationId,
            string logPath,
            CancellationToken cancellationToken = default)
        {
            _ = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Format("Проект «{0}» не найден.", projectId));

            var version = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Format("Версия «{0}» не найдена.", versionId));

            if (version.KKProjectId != projectId)
            {
                throw new ArgumentException(localizationService.Get("Версия не принадлежит указанному проекту."));
            }

            ValidateJsonObject(variablesSnapshotJson, nameof(variablesSnapshotJson));

            if (!Guid.TryParseExact(remoteOperationId, "N", out _))
            {
                throw new ArgumentException(
                    localizationService.Get("Недопустимый идентификатор удалённой операции."),
                    nameof(remoteOperationId));
            }

            var deployment = new KKProjectDeployment
            {
                Id = Guid.NewGuid(),
                KKProjectId = projectId,
                RemoteMachineId = target.RemoteMachineId,
                RemoteServiceNameSnapshot = target.ServiceName,
                RemoteDirectorySnapshot = target.Directory,
                RemoteExecutableSnapshot = target.Executable,
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
                throw new ArgumentException(localizationService.Get("Укажите конечный статус развёртывания."));
            }

            var deployment = await deploymentRepository.GetByIdAsync(
                deploymentId,
                cancellationToken) ??
                    throw new KeyNotFoundException(localizationService.Format("Операция развёртывания «{0}» не найдена.", deploymentId));

            if (deployment.Status is DeploymentStatus.Succeeded or
                DeploymentStatus.Failed or
                DeploymentStatus.Cancelled or
                DeploymentStatus.Interrupted)
            {
                throw new InvalidOperationException(localizationService.Get("Операция развёртывания уже завершена."));
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
                await notifications.ReportPersistedCompletionAsync(
                    deployment,
                    automaticRollbackCompleted,
                    automaticRollbackFailed);
            }
        }

        public async Task SaveCheckpointAsync(
            Guid deploymentId,
            DeploymentCheckpoint checkpoint)
        {
            var deployment = await deploymentRepository.GetByIdAsync(
                deploymentId,
                CancellationToken.None) ??
                    throw new KeyNotFoundException(localizationService.Format("Операция развёртывания «{0}» не найдена.", deploymentId));

            if (deployment.Status != DeploymentStatus.Running)
            {
                throw new InvalidOperationException(localizationService.Get("Операция развёртывания больше не выполняется."));
            }

            deployment.Stage = checkpoint.Stage;
            deployment.UnitChange = checkpoint.UnitChange;

            await deploymentRepository.UpdateAsync(deployment, CancellationToken.None);
        }

        private void ValidateJsonObject(string json, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException(localizationService.Get("Требуется JSON-объект."), parameterName);
            }

            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(localizationService.Get("Требуется JSON-объект."), parameterName);
            }
        }
    }
}
