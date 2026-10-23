using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        IRemoteMachineService machineService,
        IArtifactStorageActivity storageActivity,
        string? logsDirectory = null) : IDeploymentRecoveryService
    {
        private const string RecoveryLogErrorKey = "RecoveryLogError";

        public async Task<KKProjectDeployment> GetAsync(Guid deploymentId, CancellationToken cancellationToken = default)
        {
            var deployment = await deploymentRepository.GetByIdAsync(deploymentId, cancellationToken) ??
                throw new KeyNotFoundException(Texts.RecoveryNotRequired);
            if (deployment.RecoveryRequired)
            {
                deployment.RecoverySuperseded = !await deploymentRepository.IsRecoveryCurrentAsync(deploymentId, cancellationToken);
                deployment.RecoveryInProgress = operationQueue.HasActiveOperation(deployment.KKProjectId);
            }
            return deployment;
        }

        public async Task<string?> ReadLogAsync(Guid deploymentId, CancellationToken cancellationToken = default)
        {
            var deployment = await GetAsync(deploymentId, cancellationToken);
            var path = DeploymentRecoveryAttempt.Read(deployment.RecoveryAttemptsJson).LastOrDefault()?.LogPath;
            if (path is null)
            {
                return null;
            }
            try
            {
                var root = Path.GetFullPath(logsDirectory ?? DatabasePaths.LogsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) ||
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    return Texts.OperationLogFileMissingUnavailable;
                }
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
                NotSupportedException or System.Security.SecurityException)
            {
                return string.Format(Texts.RecoveryLogUnavailable, exception.Message);
            }
        }

        public async Task<KKProjectDeployment> RecoverFailedAsync(Guid deploymentId, CancellationToken cancellationToken = default)
        {
            var deployment = await GetAsync(deploymentId, cancellationToken);
            if (deployment.Status != DeploymentStatus.Failed || !deployment.RecoveryRequired ||
                !Enum.IsDefined(deployment.Stage) || deployment.Stage < DeploymentStage.SwitchingVersion ||
                deployment.Stage == DeploymentStage.Committed || !Enum.IsDefined(deployment.UnitChange))
            {
                throw new InvalidOperationException(Texts.RecoveryNotRequired);
            }
            if (deployment.RecoverySuperseded)
            {
                throw new InvalidOperationException(Texts.RecoverySuperseded);
            }
            await using var storageReservation = storageActivity.AcquireOperationLease(
                Texts.RollbackCannotStartWhileLocalVersionsBeing, cancellationToken);
            await using var machineReservation = await machineService.ReserveAsync(deployment.RemoteMachineId, cancellationToken);
            return await operationQueue.EnqueueAsync(deployment.KKProjectId, deployment.VersionTag, deployment.OperationType,
                token => RecoverFailedCoreAsync(deploymentId, token), cancellationToken, deployment.RemoteMachineId);
        }

        private async Task<KKProjectDeployment> RecoverFailedCoreAsync(Guid deploymentId, CancellationToken cancellationToken)
        {
            var deployment = await GetAsync(deploymentId, cancellationToken);
            var directory = logsDirectory ?? DatabasePaths.LogsDirectory;
            Directory.CreateDirectory(directory);
            var attempt = new DeploymentRecoveryAttempt(Guid.NewGuid(), DateTime.UtcNow, null,
                DeploymentRecoveryAttemptStatus.Running, null,
                Path.Combine(directory, $"recovery-{deploymentId:N}-{Guid.NewGuid():N}.log"), null);
            await deploymentRepository.BeginRecoveryAsync(deploymentId, attempt, cancellationToken);
            try
            {
                await using var log = new StreamWriter(attempt.LogPath, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                var machine = await machineService.GetAsync(deployment.RemoteMachineId, cancellationToken);
                if (machine.ServerGeneration != deployment.RemoteServerGeneration ||
                    !string.Equals(machine.Host, deployment.RemoteHost, StringComparison.OrdinalIgnoreCase) ||
                    machine.Port != deployment.RemotePort ||
                    machine.UserName != deployment.RemoteUserName)
                {
                    throw new InvalidOperationException(Texts.RecoveryTargetChanged);
                }
                var connection = await machineService.GetConnectionAsync(deployment.RemoteMachineId, cancellationToken);
                if (!string.Equals(connection.Host, deployment.RemoteHost, StringComparison.OrdinalIgnoreCase) ||
                    connection.Port != deployment.RemotePort || connection.UserName != deployment.RemoteUserName)
                {
                    throw new InvalidOperationException(Texts.RecoveryTargetChanged);
                }
                var project = new KKProject
                {
                    Id = deployment.KKProjectId,
                    RemoteMachineId = deployment.RemoteMachineId,
                    RemoteServiceName = deployment.RemoteServiceNameSnapshot,
                    RemoteDeploymentDirectory = deployment.RemoteDirectorySnapshot,
                    RemoteExecutableFileName = deployment.RemoteExecutableSnapshot,
                };
                var result = await remoteDeploymentService.RecoverFailedAsync(project, connection,
                    deployment.RemoteOperationId, deployment.Stage, deployment.UnitChange, log, cancellationToken);
                if (result.Outcome is not (DeploymentRecoveryOutcome.PreviousVersionRestored or
                    DeploymentRecoveryOutcome.NewVersionRemoved or DeploymentRecoveryOutcome.PreviousFilesRestored or DeploymentRecoveryOutcome.StagingCleaned))
                {
                    throw new InvalidOperationException(Texts.RecoveryOutcomeUnknown);
                }
                await deploymentRepository.CompleteRecoveryAsync(deploymentId,
                    attempt with { CompletedAtUtc = DateTime.UtcNow, Status = DeploymentRecoveryAttemptStatus.Succeeded, Outcome = result.Outcome },
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                Exception? logError = null;
                try
                {
                    await File.AppendAllTextAsync(attempt.LogPath, exception + Environment.NewLine, CancellationToken.None);
                }
                catch (Exception loggingException)
                {
                    logError = loggingException;
                    exception.Data[RecoveryLogErrorKey] = loggingException;
                }
                try
                {
                    await deploymentRepository.CompleteRecoveryAsync(deploymentId,
                        attempt with
                        {
                            CompletedAtUtc = DateTime.UtcNow,
                            Status = exception is OperationCanceledException ?
                                DeploymentRecoveryAttemptStatus.Cancelled :
                                DeploymentRecoveryAttemptStatus.Failed,
                            ErrorMessage = logError is null ?
                                exception.Message :
                                exception.Message + Environment.NewLine + string.Format(Texts.RecoveryLogUnavailable, logError.Message),
                        }, CancellationToken.None);
                }
                catch (Exception completionException)
                {
                    throw new AggregateException(Texts.RecoveryOutcomeUnknown, logError is null ?
                        new Exception[] { exception, completionException } :
                        new Exception[] { exception, logError, completionException });
                }
                throw;
            }
            return await GetAsync(deploymentId, CancellationToken.None);
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
                await using var storageReservation = storageActivity.AcquireOperationLease(
                    Texts.RollbackCannotStartWhileLocalVersionsBeing,
                    cancellationToken);
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
            if (!await deploymentRepository.IsRecoveryCurrentAsync(deployment.Id, cancellationToken))
            {
                throw new InvalidOperationException(Texts.RecoverySuperseded);
            }
            var effectiveLogsDirectory = logsDirectory ?? DatabasePaths.LogsDirectory;
            var logPath = string.IsNullOrWhiteSpace(deployment.LogPath) ?
                Path.Combine(
                    effectiveLogsDirectory,
                    $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{deployment.Id:N}.log") :
                    deployment.LogPath;

            Directory.CreateDirectory(effectiveLogsDirectory);

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
                DeploymentRecoveryOutcome.NewVersionRemoved => Texts.RecoveryNewVersionRemoved,
                DeploymentRecoveryOutcome.PreviousFilesRestored => Texts.RecoveryPreviousFilesRestored,
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
