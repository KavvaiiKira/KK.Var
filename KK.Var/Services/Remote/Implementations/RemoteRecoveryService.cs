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
        public Task<DeploymentRecoveryResult> RecoverFailedAsync(
            KKProject project, RemoteMachineSettings settings, string operationId, DeploymentStage stage,
            DeploymentUnitChange unitChange, TextWriter log, CancellationToken cancellationToken = default) =>
            Task.Run(() => Recover(project, settings, operationId, stage, unitChange, log, cancellationToken, explicitRecovery: true), cancellationToken);

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
            TextWriter log,
            string operationId)
        {
            try
            {
                var target = staging[..^(".staging-".Length + operationId.Length)];
                commandRunner.RunChecked(ssh, RemoteRecoveryProtocol.CleanupOwned(target, operationId), log);
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
            TextWriter log,
            string operationId)
        {
            try
            {
                var target = marker[..^DeploymentConstants.MarkerRelativePath.Length];
                commandRunner.RunChecked(ssh, RemoteRecoveryProtocol.CommitOwned(target, operationId), log);
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

            commandRunner.Run(ssh, $"sudo systemctl stop -- {Q(project.RemoteServiceName)}", log);
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
                    $"sudo systemctl restart -- {Q(project.RemoteServiceName)}",
                    log);
                commandRunner.RunChecked(
                    ssh,
                    $"sudo systemctl is-active --quiet -- {Q(project.RemoteServiceName)}",
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
            CancellationToken cancellationToken,
            bool explicitRecovery = false)
        {
            validator.ValidateConnection(settings, project);
            validator.ValidateOperationId(operationId);
            if (!Enum.IsDefined(stage) || !Enum.IsDefined(unitChange) ||
                (explicitRecovery && (stage < DeploymentStage.SwitchingVersion || stage == DeploymentStage.Committed)))
            {
                throw new InvalidOperationException(Texts.RecoveryNotRequired);
            }
            cancellationToken.ThrowIfCancellationRequested();

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

                if (commandRunner.Run(ssh, RemoteCommands.CheckRecoveryTools, log).ExitStatus != 0)
                {
                    throw new InvalidOperationException(Texts.RecoveryToolMissing);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var result = commandRunner.Run(ssh, RemoteRecoveryProtocol.RecoverAtCheckpoint(project, operationId, stage, unitChange), log,
                    TimeSpan.FromMinutes(2));
                var lastLine = result.Result.Trim().Split('\n')[^1].Trim();
                if (result.ExitStatus != 0 || !int.TryParse(lastLine, out var code) || code is < 1 or > 5 ||
                    (explicitRecovery && code == 1))
                {
                    throw new InvalidOperationException(Texts.RecoveryOutcomeUnknown);
                }
                return new DeploymentRecoveryResult((DeploymentRecoveryOutcome)code);
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
