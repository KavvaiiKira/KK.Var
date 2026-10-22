using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class DeploymentRecoveryService(
        IKKProjectRepository projectRepository,
        IKKProjectDeploymentRepository deploymentRepository,
        IDeploymentJournal journal,
        IRemoteDeploymentService remoteDeploymentService,
        IDeploymentOperationQueue operationQueue,
        IRemoteMachineService machineService) : IDeploymentRecoveryService
    {
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
                await using var machineReservation = await machineService.ReserveAsync(
                    deployment.RemoteMachineId,
                    cancellationToken);
                var machine = await machineService.GetAsync(deployment.RemoteMachineId, cancellationToken);
                if (machine.ServerGeneration != deployment.RemoteServerGeneration)
                {
                    throw new InvalidOperationException(Texts.PreviousServerSStateUnknownRecoveryStopped);
                }
                var connection = await machineService.GetConnectionAsync(
                    deployment.RemoteMachineId,
                    cancellationToken);
                deployment.Project = await projectRepository.GetByIdAsync(
                    deployment.KKProjectId,
                    cancellationToken) ??
                    throw new KeyNotFoundException(Texts.ProjectNotFound);
                deployment.Project.RemoteDeploymentDirectory = deployment.RemoteDirectorySnapshot;
                deployment.Project.RemoteServiceName = deployment.RemoteServiceNameSnapshot;
                deployment.Project.RemoteExecutableFileName = deployment.RemoteExecutableSnapshot;
                recoveredCount += await operationQueue.EnqueueAsync(
                    deployment.KKProjectId,
                    deployment.VersionTag,
                    deployment.OperationType,
                    token => RecoverInterruptedCoreAsync(deployment, connection, token),
                    cancellationToken,
                    remoteMachineId: deployment.RemoteMachineId);
            }

            return recoveredCount;
        }

        private async Task<int> RecoverInterruptedCoreAsync(
            KKProjectDeployment deployment,
            RemoteMachineSettings connection,
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
                connection,
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
                    Texts.InterruptedDeployCompletedNewVersionRunning,
                DeploymentRecoveryOutcome.PreviousVersionRestored =>
                    Texts.InterruptedDeployRecoveredPreviousVersionRestored,
                _ => Texts.InterruptedDeployStoppedVersionSwitch,
            };

            await journal.CompleteAsync(
                deployment.Id,
                status,
                logPath,
                status == DeploymentStatus.Interrupted ? message : null,
                cancellationToken,
                automaticRollbackCompleted: result.Outcome == DeploymentRecoveryOutcome.PreviousVersionRestored);

            return 1;
        }
    }
}
