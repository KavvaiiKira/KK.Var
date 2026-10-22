using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using Renci.SshNet;
using static KK.Var.Services.RemoteShell;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteRecoveryService(
        IRemoteClientFactory clientFactory,
        IRemoteCommandRunner commandRunner,
        IRemoteDeploymentValidator validator,
        ILocalizationService localizationService) : IRemoteRecoveryService
    {
        public Task<DeploymentRecoveryResult> RecoverAsync(
            KKProject project,
            RemoteMachineSettings settings,
            string operationId,
            DeploymentStage stage,
            DeploymentUnitChange unitChange,
            TextWriter log,
            CancellationToken cancellationToken = default) => Task.Run(
            () => Recover(
                project,
                settings,
                operationId,
                stage,
                unitChange,
                log,
                cancellationToken),
            cancellationToken);

        public void CleanupRemoteOperationFiles(
            SshClient ssh,
            string staging,
            string remoteArchive,
            string remoteUnit,
            string unitBackup,
            TextWriter log)
        {
            try
            {
                commandRunner.Run(
                    ssh,
                    $"sudo rm -rf -- {Q(staging)} {Q(remoteArchive)} {Q(remoteUnit)} {Q(unitBackup)}",
                    log);
            }
            catch (Exception exception)
            {
                log.WriteLine(localizationService.Format(
                    Texts.CouldNotRemoveTemporaryRemoteOperationFiles,
                    exception));
            }
        }

        public void CleanupCommittedDeploymentFiles(
            SshClient ssh,
            string backup,
            string remoteArchive,
            string remoteUnit,
            string unitBackup,
            string marker,
            TextWriter log)
        {
            try
            {
                commandRunner.Run(
                    ssh,
                    $"sudo rm -rf -- {Q(backup)} {Q(remoteArchive)} {Q(remoteUnit)} " +
                                    $"{Q(unitBackup)} {Q(marker)}",
                    log);
            }
            catch (Exception exception)
            {
                log.WriteLine(localizationService.Format(
                    Texts.CouldNotRemoveTemporaryFilesCompletedDeploy,
                    exception));
            }
        }

        public string RestorePreviousVersion(
            SshClient ssh,
            KKProject project,
            string target,
            string backup,
            string unitPath,
            string unitBackup,
            DeploymentUnitChange unitState,
            TextWriter log,
            IProgress<DeploymentProgress>? progress)
        {
            var previousVersionExists = commandRunner.Run(
                ssh,
                $"sudo test -e {Q(backup)}",
                log).ExitStatus == 0;

            commandRunner.Run(ssh, $"sudo systemctl stop {Q(project.RemoteServiceName)}", log);
            commandRunner.RunChecked(ssh, $"sudo rm -rf -- {Q(target)}", log);

            if (previousVersionExists)
            {
                commandRunner.RunChecked(ssh, $"sudo mv -- {Q(backup)} {Q(target)}", log);
            }

            if (unitState == DeploymentUnitChange.Created)
            {
                commandRunner.RunChecked(
                    ssh,
                    $"sudo rm -f -- {Q(unitPath)} && sudo systemctl daemon-reload",
                    log);
            }
            else if (unitState == DeploymentUnitChange.Changed)
            {
                commandRunner.RunChecked(ssh, $"sudo test -f {Q(unitBackup)}", log);
                commandRunner.RunChecked(
                    ssh,
                    $"sudo install -o root -g root -m 0644 {Q(unitBackup)} {Q(unitPath)} && " +
                                    RemoteCommands.ReloadSystemd,
                    log);
            }

            if (previousVersionExists && unitState != DeploymentUnitChange.Created)
            {
                commandRunner.RunChecked(
                    ssh,
                    $"sudo systemctl restart {Q(project.RemoteServiceName)}",
                    log);
                commandRunner.RunChecked(
                    ssh,
                    $"sudo systemctl is-active --quiet {Q(project.RemoteServiceName)}",
                    log);

                progress?.Report(new DeploymentProgress(
                    0,
                    Texts.PreviousVersionRestoredStarted));

                return Texts.PreviousVersionRestoredStartedSuccessfully;
            }

            if (previousVersionExists)
            {
                progress?.Report(new DeploymentProgress(
                    0,
                    Texts.PreviousVersionRestoredWithoutStartingService));

                return Texts.PreviousVersionRestoredButSystemdUnitDid;
            }

            progress?.Report(new DeploymentProgress(
                0,
                Texts.NoPreviousVersionNewVersionRemoved));

            return Texts.ThereNoPreviousVersionNewVersionStopped;
        }

        private DeploymentRecoveryResult Recover(
            KKProject project,
            RemoteMachineSettings settings,
            string operationId,
            DeploymentStage stage,
            DeploymentUnitChange unitChange,
            TextWriter log,
            CancellationToken cancellationToken)
        {
            validator.ValidateConnection(settings, project);
            validator.ValidateOperationId(operationId);
            cancellationToken.ThrowIfCancellationRequested();

            var target = project.RemoteDeploymentDirectory.TrimEnd('/');
            var staging = $"{target}.staging-{operationId}";
            var backup = $"{target}.previous-{operationId}";
            var remoteArchive = $"{DeploymentConstants.RemoteTemporaryPrefix}{operationId}{ArtifactConstants.ArchiveExtension}";
            var remoteUnit = $"{DeploymentConstants.RemoteTemporaryPrefix}{operationId}{DeploymentConstants.ServiceExtension}";
            var unitPath = $"{DeploymentConstants.SystemdDirectory}{project.RemoteServiceName}";
            var unitBackup = $"{DeploymentConstants.RemoteTemporaryPrefix}{operationId}{DeploymentConstants.ServiceBackupExtension}";
            var marker = target + DeploymentConstants.MarkerRelativePath;

            using var ssh = clientFactory.CreateSshClient(settings);

            var hostKeyValidator = new SshHostKeyValidator(settings.HostKeyFingerprint);

            hostKeyValidator.Attach(ssh);
            ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);

            try
            {
                ssh.Connect();
            }
            catch (Renci.SshNet.Common.SshConnectionException) when (
                hostKeyValidator.RequiresConfirmation)
            {
                throw new InvalidOperationException(Texts.SSHServerKeyDoesNotMatchTrusted);
            }

            try
            {
                commandRunner.RunChecked(ssh, RemoteCommands.CheckSudo, log);

                var markerMatches = commandRunner.Run(
                    ssh,
                    $"sudo test -f {Q(marker)} && test \"$(sudo cat {Q(marker)})\" = {Q(operationId)}",
                    log).ExitStatus == 0;

                var serviceIsActive = commandRunner.Run(
                    ssh,
                    $"sudo systemctl is-active --quiet {Q(project.RemoteServiceName)}",
                    log).ExitStatus == 0;

                if (stage == DeploymentStage.Committed && serviceIsActive)
                {
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo rm -rf -- {Q(backup)} {Q(staging)} {Q(remoteArchive)} {Q(remoteUnit)} " +
                                            $"{Q(unitBackup)} {Q(marker)}",
                        log);
                    return new DeploymentRecoveryResult(
                        DeploymentRecoveryOutcome.NewVersionActive);
                }

                if (stage is DeploymentStage.StartingService or
                    DeploymentStage.ServiceStarted or
                    DeploymentStage.HealthChecking)
                {
                    commandRunner.CaptureFailureDiagnostics(ssh, project, log);
                }

                if (stage < DeploymentStage.SwitchingVersion)
                {
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo rm -rf -- {Q(staging)} {Q(remoteArchive)} {Q(remoteUnit)} {Q(unitBackup)}",
                        log);
                    return new DeploymentRecoveryResult(
                        DeploymentRecoveryOutcome.StagingCleaned);
                }

                var backupExists = commandRunner.Run(
                    ssh,
                    $"sudo test -e {Q(backup)}",
                    log).ExitStatus == 0;

                var targetExists = commandRunner.Run(
                    ssh,
                    $"sudo test -e {Q(target)}",
                    log).ExitStatus == 0;

                var hasPreviousVersion = backupExists || (targetExists && !markerMatches);

                commandRunner.Run(
                    ssh,
                    $"sudo systemctl stop {Q(project.RemoteServiceName)}; " +
                                    $"if sudo test -f {Q(marker)}; then sudo rm -rf -- {Q(target)}; fi; " +
                                    $"if sudo test -e {Q(backup)}; then sudo mv -- {Q(backup)} {Q(target)}; fi",
                    log);

                if (unitChange == DeploymentUnitChange.Created)
                {
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo rm -f -- {Q(unitPath)} && sudo systemctl daemon-reload",
                        log);
                }
                else if (unitChange == DeploymentUnitChange.Changed)
                {
                    commandRunner.RunChecked(
                        ssh,
                        $"if sudo test -f {Q(unitBackup)}; then " +
                                            $"sudo install -o root -g root -m 0644 {Q(unitBackup)} {Q(unitPath)} && " +
                                            RemoteCommands.ReloadSystemd + "; fi",
                        log);
                }

                if (hasPreviousVersion && unitChange != DeploymentUnitChange.Created)
                {
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo systemctl restart {Q(project.RemoteServiceName)}",
                        log);
                    commandRunner.RunChecked(
                        ssh,
                        $"sudo systemctl is-active --quiet {Q(project.RemoteServiceName)}",
                        log);
                }

                commandRunner.RunChecked(
                    ssh,
                    $"sudo rm -rf -- {Q(staging)} {Q(remoteArchive)} {Q(remoteUnit)} {Q(unitBackup)}",
                    log);

                return new DeploymentRecoveryResult(
                    hasPreviousVersion && unitChange != DeploymentUnitChange.Created ?
                        DeploymentRecoveryOutcome.PreviousVersionRestored :
                        DeploymentRecoveryOutcome.StagingCleaned);
            }
            finally
            {
                if (ssh.IsConnected)
                {
                    ssh.Disconnect();
                }
            }
        }
    }
}
