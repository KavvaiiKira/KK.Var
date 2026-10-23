using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using Renci.SshNet;
using Renci.SshNet.Common;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteConnectionService(
        IRemoteClientFactory clientFactory,
        ILocalizationService localizationService)
        : IRemoteConnectionService
    {
        private const string FreeSpaceProbeScript = "while ! sudo -n test -d \"$check_path\"; do parent=$(dirname -- \"$check_path\"); if test \"$parent\" = \"$check_path\"; then break; fi; check_path=\"$parent\"; done; df -Pk -- \"$check_path\" | awk 'NR==2 {print $4}'";

        public Task<RemoteConnectionCheckResult> CheckAsync(
            RemoteMachineSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            return Task.Run(() => Check(settings), cancellationToken);
        }

        public Task<IReadOnlyList<DeploymentPreflightCheck>> CheckDeploymentAsync(
            RemoteMachineSettings settings,
            string deploymentDirectory,
            long minimumFreeBytes,
            bool requiresPython,
            CancellationToken cancellationToken = default) =>
            Task.Run(
                () => CheckDeployment(
                    settings,
                    deploymentDirectory,
                    minimumFreeBytes,
                    requiresPython,
                    cancellationToken),
                cancellationToken);

        private IReadOnlyList<DeploymentPreflightCheck> CheckDeployment(
            RemoteMachineSettings settings,
            string deploymentDirectory,
            long minimumFreeBytes,
            bool requiresPython,
            CancellationToken cancellationToken)
        {
            var checks = new List<DeploymentPreflightCheck>();

            if (string.IsNullOrWhiteSpace(settings.HostKeyFingerprint) ||
                !string.Equals(
                    settings.HostKeyHost,
                    settings.Host?.Trim(),
                    StringComparison.OrdinalIgnoreCase) ||
                settings.HostKeyPort != settings.Port)
            {
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Ssh,
                    PreflightStatus.Error,
                    Texts.ConfirmSSHHostFingerprintSettings));
                return checks;
            }

            try
            {
                using var client = clientFactory.CreateSshClient(settings);
                var validator = new SshHostKeyValidator(settings.HostKeyFingerprint);
                validator.Attach(client);
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                cancellationToken.ThrowIfCancellationRequested();
                client.Connect();

                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Ssh,
                    PreflightStatus.Success,
                    Texts.SSHConnectionHostFingerprintVerified));

                var architecture = Run(client, RemoteCommands.Architecture);
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Architecture,
                    architecture.ExitStatus == 0 &&
                    string.Equals(
                        ArchitectureCompatibility.Normalize(architecture.Result),
                        ArchitectureCompatibility.Normalize(settings.Architecture),
                        StringComparison.OrdinalIgnoreCase) ?
                        PreflightStatus.Success :
                        PreflightStatus.Error,
                    localizationService.Get(architecture.ExitStatus == 0 &&
                        string.Equals(
                            ArchitectureCompatibility.Normalize(architecture.Result),
                            ArchitectureCompatibility.Normalize(settings.Architecture),
                            StringComparison.OrdinalIgnoreCase) ?
                        Texts.RemoteArchitectureChecked :
                        Texts.RemoteArchitectureDoesNotMatchSettings),
                    architecture.Result.Trim()));

                cancellationToken.ThrowIfCancellationRequested();
                var sudo = Run(client, RemoteCommands.CheckSudo);
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Sudo,
                    sudo.ExitStatus == 0 ? PreflightStatus.Success : PreflightStatus.Error,
                    localizationService.Get(sudo.ExitStatus == 0 ?
                        Texts.NonInteractiveSudoChecked :
                        Texts.NonInteractiveSudoUnavailable),
                    sudo.ExitStatus == 0 ? null : sudo.Error.Trim()));

                cancellationToken.ThrowIfCancellationRequested();
                var systemd = Run(
                    client,
                    RemoteCommands.CheckRunningSystemd);
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Systemd,
                    systemd.ExitStatus == 0 ? PreflightStatus.Success : PreflightStatus.Error,
                    localizationService.Get(systemd.ExitStatus == 0 ?
                        Texts.SystemdTarAvailable :
                        Texts.SystemdTarUnavailable),
                    systemd.ExitStatus == 0 ? null : systemd.Error.Trim()));

                if (requiresPython)
                {
                    var python = Run(client, RemoteCommands.CheckPython);
                    checks.Add(new DeploymentPreflightCheck(
                        PreflightCheckKeys.Python,
                        python.ExitStatus == 0 ? PreflightStatus.Success : PreflightStatus.Error,
                        localizationService.Get(python.ExitStatus == 0 ?
                            Texts.Python3AvailableRemoteMachine :
                            Texts.Python3UnavailableRemoteMachine),
                        python.ExitStatus == 0 ? null : python.Error.Trim()));
                }

                if (sudo.ExitStatus == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var quotedDirectory = RemoteShell.Q(deploymentDirectory);
                    var disk = Run(
                        client,
                        $"check_path=$(dirname -- {quotedDirectory}); " +
                                            FreeSpaceProbeScript);
                    var available = long.TryParse(
                        disk.Result.Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var kilobytes) ?
                        kilobytes * 1024 :
                        -1;
                    checks.Add(new DeploymentPreflightCheck(
                        PreflightCheckKeys.RemoteSpace,
                        disk.ExitStatus == 0 && available >= minimumFreeBytes ?
                            PreflightStatus.Success :
                            PreflightStatus.Error,
                        localizationService.Get(disk.ExitStatus == 0 &&
                            available >= minimumFreeBytes ?
                            Texts.RemoteFreeSpaceChecked :
                            Texts.RemoteMachineDoesNotEnoughFreeSpace),
                        available < 0 ? disk.Error.Trim() :
                            localizationService.Format(
                                Texts.AvailableBytesMinimumRequired,
                                available,
                                minimumFreeBytes)));
                }

                client.Disconnect();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Ssh,
                    PreflightStatus.Error,
                    Texts.RemoteMachineCouldNotChecked,
                    exception.Message));
            }

            return checks;
        }

        private static (int? ExitStatus, string Result, string Error) Run(
            SshClient client,
            string command)
        {
            using var result = client.CreateCommand(command);
            result.CommandTimeout = TimeSpan.FromSeconds(10);
            result.Execute();
            return (result.ExitStatus, result.Result, result.Error);
        }

        private RemoteConnectionCheckResult Check(RemoteMachineSettings settings)
        {
            SshHostKeyValidator? hostKeyValidator = null;

            try
            {
                using var client = clientFactory.CreateSshClient(settings);

                hostKeyValidator = new SshHostKeyValidator(settings.HostKeyFingerprint);
                hostKeyValidator.Attach(client);

                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                client.Connect();

                using var command = client.RunCommand(RemoteCommands.Architecture);
                var architecture = command.Result.Trim();

                if (command.ExitStatus != 0 || string.IsNullOrWhiteSpace(architecture))
                {
                    return RemoteConnectionCheckResult.Failure(Texts.SSHConnectedButMachineArchitectureCouldNot);
                }

                client.Disconnect();
                return RemoteConnectionCheckResult.Success(
                    architecture,
                    hostKeyValidator.ObservedFingerprint);
            }
            catch (SshConnectionException) when (
                hostKeyValidator is { RequiresConfirmation: true })
            {
                return RemoteConnectionCheckResult.ConfirmationRequired(
                    hostKeyValidator.ObservedFingerprint);
            }
            catch (SshAuthenticationException)
            {
                return RemoteConnectionCheckResult.Failure(Texts.SSHServerRejectedSuppliedCredentials);
            }
            catch (SshConnectionException exception)
            {
                return RemoteConnectionCheckResult.Failure(
                    localizationService.Format(
                        Texts.CouldNotEstablishSSHConnection,
                        exception.Message));
            }
            catch (SocketException exception)
            {
                return RemoteConnectionCheckResult.Failure(
                    localizationService.Format(
                        Texts.RemoteMachineUnavailable,
                        exception.Message));
            }
            catch (FileNotFoundException)
            {
                return RemoteConnectionCheckResult.Failure(Texts.PrivateSSHKeyFileNotFound);
            }
            catch (UnauthorizedAccessException)
            {
                return RemoteConnectionCheckResult.Failure(Texts.PrivateSSHKeyFileCannotAccessed);
            }
            catch (Exception exception)
            {
                return RemoteConnectionCheckResult.Failure(
                    localizationService.Format(
                        Texts.ConnectionCheckFailed,
                        exception.Message));
            }
        }
    }
}
