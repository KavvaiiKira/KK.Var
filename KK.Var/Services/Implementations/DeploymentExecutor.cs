using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    public sealed class DeploymentExecutor(
        IKKProjectDeploymentRepository deploymentRepository,
        IDeploymentJournal journal,
        IKKProjectEnvironmentService environmentService,
        IRemoteDeploymentService remoteDeploymentService,
        IRemoteMachineService machineService) : IDeploymentExecutor
    {
        public async Task<KKProjectDeployment> ExecuteAsync(
            KKProject project,
            KKProjectVersion version,
            string artifactPath,
            DeploymentOperationType operationType,
            RemoteMachineSettings connection,
            IProgress<DeploymentProgress>? progress,
            CancellationToken cancellationToken)
        {
            if (!ArchitectureCompatibility.IsCompatible(version.TargetArchitecture, connection.Architecture))
            {
                throw new InvalidOperationException(Texts.ArchiveArchitectureIncompatibleSelectedMachine);
            }
            var machine = await machineService.GetAsync(project.RemoteMachineId, cancellationToken);
            connection.CleanDeployment = machine.ServerGeneration > 0 &&
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

            var deployment = await journal.StartAsync(
                project.Id,
                version.Id,
                operationType,
                new DeploymentTargetSnapshot(
                    project.RemoteMachineId,
                    project.RemoteServiceName,
                    project.RemoteDeploymentDirectory,
                    project.RemoteExecutableFileName),
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
                    new System.Text.UTF8Encoding(false))
                {
                    AutoFlush = true,
                };

                await remoteDeploymentService.DeployAsync(
                    project,
                    artifactPath,
                    connection,
                    remoteOperationId,
                    log,
                    checkpoint => journal.SaveCheckpointAsync(deployment.Id, checkpoint),
                    progress,
                    cancellationToken);

                remoteCommitted = true;

                await journal.CompleteAsync(
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

                await journal.CompleteAsync(
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
    }
}
