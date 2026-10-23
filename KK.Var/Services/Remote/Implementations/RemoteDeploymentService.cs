using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using static KK.Var.Services.ProjectRuntime;
using static KK.Var.Services.RemoteShell;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteDeploymentService(
        IBuildConfigurationService buildConfigurationService,
        IRemoteClientFactory clientFactory,
        IRemoteCommandRunner commandRunner,
        IRemoteDeploymentValidator validator,
        ISystemdUnitBuilder unitBuilder,
        IRemoteHealthCheckService healthCheckService,
        IRemoteRecoveryService recoveryService,
        ILocalizationService localizationService) : IRemoteDeploymentService
    {
        public Task<DeploymentRecoveryResult> RecoverFailedAsync(
            KKProject project, RemoteMachineSettings settings, string operationId, DeploymentStage stage,
            DeploymentUnitChange unitChange, TextWriter log, CancellationToken cancellationToken = default) =>
            recoveryService.RecoverFailedAsync(project, settings, operationId, stage, unitChange, log, cancellationToken);

        private const string FreeSpaceProbeScript = "while ! sudo test -d \"$check_path\"; do parent=$(dirname -- \"$check_path\"); if test \"$parent\" = \"$check_path\"; then break; fi; check_path=\"$parent\"; done; available=$(df -Pk -- \"$check_path\" | awk 'NR==2 {print $4}'); ";

        public Task DeployAsync(
            KKProject project,
            string artifactPath,
            RemoteMachineSettings settings,
            string operationId,
            TextWriter log,
            Func<DeploymentCheckpoint, Task> checkpoint,
            IProgress<DeploymentProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.Run(
            () => Deploy(
                project,
                artifactPath,
                settings,
                operationId,
                log,
                checkpoint,
                progress,
                cancellationToken),
            cancellationToken);

        public Task<DeploymentRecoveryResult> RecoverAsync(
            KKProject project,
            RemoteMachineSettings settings,
            string operationId,
            DeploymentStage stage,
            DeploymentUnitChange unitChange,
            TextWriter log,
            CancellationToken cancellationToken = default) =>
            recoveryService.RecoverAsync(
                project,
                settings,
                operationId,
                stage,
                unitChange,
                log,
                cancellationToken);

        private void Deploy(
            KKProject project,
            string artifactPath,
            RemoteMachineSettings settings,
            string operationId,
            TextWriter log,
            Func<DeploymentCheckpoint, Task> checkpoint,
            IProgress<DeploymentProgress>? progress,
            CancellationToken cancellationToken)
        {
            validator.Validate(settings, project, artifactPath);
            validator.ValidateOperationId(operationId);

            var remoteArchive = $"{DeploymentConstants.RemoteTemporaryPrefix}{operationId}{ArtifactConstants.ArchiveExtension}";
            var remoteUnit = $"{DeploymentConstants.RemoteTemporaryPrefix}{operationId}{DeploymentConstants.ServiceExtension}";
            var target = project.RemoteDeploymentDirectory.TrimEnd('/');
            var staging = $"{target}.staging-{operationId}";
            var backup = $"{target}.previous-{operationId}";
            var unitPath = $"{DeploymentConstants.SystemdDirectory}{project.RemoteServiceName}";
            var unitBackup = $"{DeploymentConstants.RemoteTemporaryPrefix}{operationId}{DeploymentConstants.ServiceBackupExtension}";
            var unitContent = unitBuilder.CreateUnit(project, settings.UserName!);
            var unitState = DeploymentUnitChange.Unchanged;
            var targetMoved = false;
            var switchAttempted = false;
            var healthCheckInProgress = false;

            using var ssh = clientFactory.CreateSshClient(settings);
            using var sftp = clientFactory.CreateSftpClient(settings);

            var sshHostKeyValidator = new SshHostKeyValidator(settings.HostKeyFingerprint);
            var sftpHostKeyValidator = new SshHostKeyValidator(settings.HostKeyFingerprint);

            sshHostKeyValidator.Attach(ssh);
            sftpHostKeyValidator.Attach(sftp);

            ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
            sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);

            try
            {
                ssh.Connect();
                sftp.Connect();
            }
            catch (Renci.SshNet.Common.SshConnectionException) when (
                sshHostKeyValidator.RequiresConfirmation ||
                sftpHostKeyValidator.RequiresConfirmation)
            {
                throw new InvalidOperationException(Texts.SSHServerKeyDoesNotMatchTrusted);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new DeploymentProgress(
                    55,
                    Texts.CheckingRemoteMachine));

                commandRunner.RunChecked(ssh, RemoteCommands.CheckSudo, log);
                if (commandRunner.Run(ssh, RemoteCommands.CheckRecoveryTools, log).ExitStatus != 0)
                {
                    throw new InvalidOperationException(Texts.RecoveryToolMissing);
                }
                commandRunner.RunChecked(ssh, RemoteCommands.CheckDeploymentTools, log);

                var actualArchitecture = commandRunner.RunCheckedWithOutput(ssh, RemoteCommands.Architecture, log);

                if (!string.Equals(
                    ArchitectureCompatibility.Normalize(actualArchitecture),
                    ArchitectureCompatibility.Normalize(settings.Architecture),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(localizationService.Format(
                        Texts.RemoteMachineArchitectureChangedExpectedReceivedRun,
                        ArchitectureCompatibility.Normalize(settings.Architecture),
                        actualArchitecture));
                }

                var requiredKilobytes = Math.Max(1024, new FileInfo(artifactPath).Length / 1024 * 3);

                commandRunner.RunChecked(
                    ssh,
                    $"check_path=$(dirname -- {Q(target)}); " +
                                    FreeSpaceProbeScript +
                                    $"test -n \"$available\" && test \"$available\" -ge {requiredKilobytes}",
                    log);

                commandRunner.RunChecked(ssh, RemoteRecoveryProtocol.PrepareOperation(target, operationId), log);
                progress?.Report(new DeploymentProgress(
                    58,
                    Texts.RemoteMachineReadyDeploy));
                progress?.Report(new DeploymentProgress(
                    60,
                    Texts.UploadingArchiveRemoteMachine));

                using (var archive = File.OpenRead(artifactPath))
                {
                    sftp.UploadFile(archive, remoteArchive);
                }

                using (var unit = new MemoryStream(Encoding.UTF8.GetBytes(unitContent)))
                {
                    sftp.UploadFile(unit, remoteUnit);
                }

                progress?.Report(new DeploymentProgress(
                    65,
                    Texts.ExtractingNewVersion));

                commandRunner.RunChecked(
                    ssh,
                    $"sudo rm -rf -- {Q(staging)} && sudo mkdir -p -- {Q(staging)} && " +
                                    $"sudo tar -xzf {Q(remoteArchive)} -C {Q(staging)} && " +
                                    $"printf '%s' {Q(operationId)} | sudo tee {Q(staging + DeploymentConstants.MarkerRelativePath)} >/dev/null && " +
                                    $"sudo chown -R {Q(settings.UserName!)} -- {Q(staging)}",
                    log);

                if (IsPythonProject(project))
                {
                    var buildConfiguration = buildConfigurationService.ReadAndValidate(
                        project.BuildConfigurationJson,
                        ProjectBuildProvider.Python);

                    var pipArguments = string.Join(
                        ' ',
                        (buildConfiguration.BuildArguments ?? []).Select(Q));

                    var pipArgumentPrefix =
                        string.IsNullOrWhiteSpace(pipArguments) ?
                            string.Empty :
                            pipArguments + " ";

                    var dependency = buildConfiguration.PythonDependencyFilePath;
                    var installCommand = string.IsNullOrWhiteSpace(dependency) ?
                        $"if test -f {Q(staging + "/" + BuildConstants.PythonRequirementsFileName)}; then " +
                        $"sudo -u {Q(settings.UserName!)} {Q(staging + BuildConstants.PythonPipRelativePath)} install {pipArgumentPrefix}-r {Q(staging + "/" + BuildConstants.PythonRequirementsFileName)}; " +
                        $"elif test -f {Q(staging + "/" + BuildConstants.PythonProjectFileName)}; then " +
                        $"sudo -u {Q(settings.UserName!)} {Q(staging + BuildConstants.PythonPipRelativePath)} install {pipArgumentPrefix}{Q(staging)}; fi" :
                        $"test -f {Q(staging + "/" + dependency.Replace('\\', '/'))} && " +
                        $"sudo -u {Q(settings.UserName!)} {Q(staging + BuildConstants.PythonPipRelativePath)} install {pipArgumentPrefix}" +
                        (dependency.EndsWith(BuildConstants.TextFileExtension, StringComparison.OrdinalIgnoreCase) ?
                            $"-r {Q(staging + "/" + dependency.Replace('\\', '/'))}" :
                            Q(staging + "/" + (Path.GetDirectoryName(dependency)?.Replace('\\', '/') ?? ".")));

                    commandRunner.RunChecked(ssh, RemoteCommands.CheckPython, log);
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo -u {Q(settings.UserName!)} python3 -m venv {Q(staging + "/" + BuildConstants.PythonVirtualEnvironmentDirectory)} && " +
                                            installCommand,
                        log);
                }
                else
                {
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo chmod u+x -- {Q(staging + "/" + GetExecutableRelativePath(project))}",
                        log);
                }

                var unitExists = !settings.CleanDeployment && commandRunner.Run(
                    ssh,
                    $"sudo test -f {Q(unitPath)}",
                    log).ExitStatus == 0;
                if (!unitExists)
                {
                    unitState = DeploymentUnitChange.Created;
                    progress?.Report(new DeploymentProgress(
                        70,
                        Texts.SystemdUnitCreated));
                }
                else if (commandRunner.Run(ssh, $"sudo cmp -s -- {Q(remoteUnit)} {Q(unitPath)}", log).ExitStatus != 0)
                {
                    unitState = DeploymentUnitChange.Changed;
                    commandRunner.RunChecked(ssh, $"sudo cp -- {Q(unitPath)} {Q(unitBackup)}", log);
                    progress?.Report(new DeploymentProgress(
                        70,
                        Texts.SystemdUnitUpdated));
                }
                else
                {
                    progress?.Report(new DeploymentProgress(
                        70,
                        Texts.SystemdUnitUnchanged));
                }

                SaveCheckpoint(checkpoint, DeploymentStage.ReadyToSwitch, unitState);

                progress?.Report(new DeploymentProgress(
                    75,
                    Texts.SwitchingVersion));

                SaveCheckpoint(checkpoint, DeploymentStage.SwitchingVersion, unitState);

                switchAttempted = true;
                commandRunner.RunChecked(ssh, RemoteRecoveryProtocol.PrepareSwitch(target, operationId, settings.CleanDeployment), log);
                commandRunner.Run(ssh, $"sudo systemctl stop -- {Q(project.RemoteServiceName)}", log);
                commandRunner.RunChecked(
                    ssh,
                    (settings.CleanDeployment ?
                        $"sudo rm -rf -- {Q(target)} && " :
                        $"if sudo test -e {Q(target)}; then sudo mv -- {Q(target)} {Q(backup)}; fi && ") +
                                    $"sudo mv -- {Q(staging)} {Q(target)}",
                    log);

                targetMoved = true;
                SaveCheckpoint(checkpoint, DeploymentStage.VersionSwitched, unitState);

                SaveCheckpoint(checkpoint, DeploymentStage.UpdatingUnit, unitState);

                if (unitState != DeploymentUnitChange.Unchanged)
                {
                    commandRunner.RunChecked(ssh, $"sudo systemd-analyze verify {Q(remoteUnit)}", log);
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo install -o root -g root -m 0644 {Q(remoteUnit)} {Q(unitPath)} && sudo systemctl daemon-reload",
                        log);
                    progress?.Report(new DeploymentProgress(
                        82,
                        Texts.SystemdReloadedUpdatedUnit));
                }

                SaveCheckpoint(checkpoint, DeploymentStage.UnitUpdated, unitState);

                if (commandRunner.Run(ssh, $"sudo systemctl is-enabled -- {Q(project.RemoteServiceName)}", log).ExitStatus != 0)
                {
                    commandRunner.RunChecked(ssh, $"sudo systemctl enable -- {Q(project.RemoteServiceName)}", log);
                }

                progress?.Report(new DeploymentProgress(88, Texts.StartingSystemdService));

                SaveCheckpoint(checkpoint, DeploymentStage.StartingService, unitState);

                commandRunner.RunChecked(ssh, $"sudo systemctl restart -- {Q(project.RemoteServiceName)}", log);
                commandRunner.RunChecked(
                    ssh,
                    $"sudo systemctl is-active --quiet -- {Q(project.RemoteServiceName)}",
                    log);

                SaveCheckpoint(checkpoint, DeploymentStage.ServiceStarted, unitState);

                if (project.HealthCheckType == ProjectHealthCheckType.None)
                {
                    progress?.Report(new DeploymentProgress(
                        96,
                        Texts.SystemdServiceStartedSuccessfully));
                }
                else
                {
                    SaveCheckpoint(checkpoint, DeploymentStage.HealthChecking, unitState);
                    healthCheckInProgress = true;

                    progress?.Report(new DeploymentProgress(
                        92,
                        Texts.RunningHealthCheck));

                    healthCheckService.RunHealthCheck(
                        ssh,
                        project,
                        settings.UserName!,
                        target,
                        log,
                        progress,
                        cancellationToken);

                    healthCheckInProgress = false;
                    progress?.Report(new DeploymentProgress(
                        96,
                        Texts.HealthCheckPassed));
                }

                SaveCheckpoint(checkpoint, DeploymentStage.Committed, unitState);

                recoveryService.CleanupCommittedDeploymentFiles(
                    ssh,
                    backup,
                    remoteArchive,
                    remoteUnit,
                    unitBackup,
                    target + DeploymentConstants.MarkerRelativePath,
                    log,
                    operationId);

                progress?.Report(new DeploymentProgress(
                    100,
                    Texts.DeployCompleted));
            }
            catch (Exception exception)
            {
                progress?.Report(new DeploymentProgress(
                    0,
                    localizationService.Get(healthCheckInProgress ?
                        Texts.HealthCheckFailedRestoringPreviousVersion :
                        Texts.DeployFailedRestoringPreviousVersion)));

                log.WriteLine(localizationService.Format(
                    Texts.RemoteOperationFailed,
                    exception));

                string? rollbackResult = null;
                Exception? rollbackException = null;

                try
                {
                    if (targetMoved)
                    {
                        commandRunner.CaptureFailureDiagnostics(ssh, project, log);
                        commandRunner.RunChecked(ssh, RemoteRecoveryProtocol.PrepareRollback(target, operationId), log);

                        rollbackResult = recoveryService.RestorePreviousVersion(
                            ssh,
                            project,
                            target,
                            backup,
                            unitPath,
                            unitBackup,
                            unitState,
                            log,
                            progress);
                    }
                }
                catch (Exception caughtRollbackException)
                {
                    rollbackException = caughtRollbackException;
                    log.WriteLine(localizationService.Format(
                        Texts.AutomaticRollbackFailed,
                        caughtRollbackException));
                }

                if (!switchAttempted || (targetMoved && rollbackException is null))
                {
                    recoveryService.CleanupRemoteOperationFiles(
                        ssh,
                        staging,
                        remoteArchive,
                        remoteUnit,
                        unitBackup,
                        log,
                        operationId);
                }
                else
                {
                    log.WriteLine(Texts.TemporaryFilesKeptRemoteMachineRecovery);
                }

                if (!targetMoved)
                {
                    throw;
                }

                if (rollbackException is not null)
                {
                    throw new RemoteDeploymentFailureException(localizationService.Format(
                        Texts.RemoteOperationFailedAutomaticRollbackFailed,
                        exception.Message,
                        rollbackException.Message),
                        new AggregateException(exception, rollbackException), automaticRollbackCompleted: false);
                }

                log.WriteLine(localizationService.Format(
                    Texts.AutomaticRollbackResult,
                    rollbackResult));

                if (exception is OperationCanceledException)
                {
                    throw new OperationCanceledException(localizationService.Format(
                        Texts.RemoteOperationCancelledAutomaticRollbackResult,
                        exception.Message,
                        rollbackResult),
                        exception,
                        cancellationToken);
                }

                throw new RemoteDeploymentFailureException(localizationService.Format(
                    Texts.RemoteOperationFailedAutomaticRollbackResult,
                    exception.Message,
                    rollbackResult),
                    exception, automaticRollbackCompleted: true);
            }
            finally
            {
                if (sftp.IsConnected)
                {
                    sftp.Disconnect();
                }

                if (ssh.IsConnected)
                {
                    ssh.Disconnect();
                }
            }
        }

        private static void SaveCheckpoint(
            Func<DeploymentCheckpoint, Task> checkpoint,
            DeploymentStage stage,
            DeploymentUnitChange unitChange) =>
            checkpoint(new DeploymentCheckpoint(stage, unitChange))
                .GetAwaiter()
                .GetResult();
    }
}
