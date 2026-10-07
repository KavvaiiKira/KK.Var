using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace KK.Var.Services.Implementations;

public sealed class RemoteConnectionService(ILocalizationService localizationService)
    : IRemoteConnectionService
{
    private const string PasswordAuthentication = "Пароль";

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
                "ssh",
                PreflightStatus.Error,
                localizationService.Get("Подтвердите отпечаток SSH-сервера в настройках.")));
            return checks;
        }

        try
        {
            using var client = CreateClient(settings);
            var validator = new SshHostKeyValidator(settings.HostKeyFingerprint);
            validator.Attach(client);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
            cancellationToken.ThrowIfCancellationRequested();
            client.Connect();

            checks.Add(new DeploymentPreflightCheck(
                "ssh",
                PreflightStatus.Success,
                localizationService.Get("SSH-подключение и отпечаток подтверждены.")));

            var architecture = Run(client, "uname -m");
            checks.Add(new DeploymentPreflightCheck(
                "architecture",
                architecture.ExitStatus == 0 &&
                string.Equals(
                    architecture.Result.Trim(),
                    settings.Architecture,
                    StringComparison.OrdinalIgnoreCase) ?
                    PreflightStatus.Success :
                    PreflightStatus.Error,
                localizationService.Get(architecture.ExitStatus == 0 &&
                    string.Equals(architecture.Result.Trim(), settings.Architecture,
                        StringComparison.OrdinalIgnoreCase) ?
                    "Архитектура удалённой машины проверена." :
                    "Архитектура удалённой машины не совпадает с настройками."),
                architecture.Result.Trim()));

            cancellationToken.ThrowIfCancellationRequested();
            var sudo = Run(client, "sudo -n true");
            checks.Add(new DeploymentPreflightCheck(
                "sudo",
                sudo.ExitStatus == 0 ? PreflightStatus.Success : PreflightStatus.Error,
                localizationService.Get(sudo.ExitStatus == 0 ?
                    "sudo без запроса пароля проверен." :
                    "sudo без запроса пароля недоступен."),
                sudo.ExitStatus == 0 ? null : sudo.Error.Trim()));

            cancellationToken.ThrowIfCancellationRequested();
            var systemd = Run(
                client,
                "command -v systemctl >/dev/null && test -d /run/systemd/system && command -v tar >/dev/null");
            checks.Add(new DeploymentPreflightCheck(
                "systemd",
                systemd.ExitStatus == 0 ? PreflightStatus.Success : PreflightStatus.Error,
                localizationService.Get(systemd.ExitStatus == 0 ?
                    "systemd и tar доступны." :
                    "systemd или tar недоступны."),
                systemd.ExitStatus == 0 ? null : systemd.Error.Trim()));

            if (requiresPython)
            {
                var python = Run(client, "command -v python3 >/dev/null");
                checks.Add(new DeploymentPreflightCheck(
                    "python",
                    python.ExitStatus == 0 ? PreflightStatus.Success : PreflightStatus.Error,
                    localizationService.Get(python.ExitStatus == 0 ?
                        "Python 3 доступен на удалённой машине." :
                        "Python 3 недоступен на удалённой машине."),
                    python.ExitStatus == 0 ? null : python.Error.Trim()));
            }

            if (sudo.ExitStatus == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var quotedDirectory = Quote(deploymentDirectory);
                var disk = Run(
                    client,
                    $"check_path=$(dirname -- {quotedDirectory}); " +
                    "while ! sudo -n test -d \"$check_path\"; do " +
                    "parent=$(dirname -- \"$check_path\"); " +
                    "if test \"$parent\" = \"$check_path\"; then break; fi; " +
                    "check_path=\"$parent\"; done; " +
                    "df -Pk -- \"$check_path\" | awk 'NR==2 {print $4}'");
                var available = long.TryParse(
                    disk.Result.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var kilobytes) ?
                    kilobytes * 1024 :
                    -1;
                checks.Add(new DeploymentPreflightCheck(
                    "remote-space",
                    disk.ExitStatus == 0 && available >= minimumFreeBytes ?
                        PreflightStatus.Success :
                        PreflightStatus.Error,
                    localizationService.Get(disk.ExitStatus == 0 &&
                        available >= minimumFreeBytes ?
                        "Свободное место на удалённой машине проверено." :
                        "На удалённой машине недостаточно свободного места."),
                    available < 0 ? disk.Error.Trim() :
                        localizationService.Format("Доступно байт: {0}; требуется минимум: {1}.",
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
                "ssh",
                PreflightStatus.Error,
                localizationService.Get("Не удалось проверить удалённую машину."),
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

    private static string Quote(string value) =>
        $"'{value.Replace("'", "'\"'\"'")}'";

    private RemoteConnectionCheckResult Check(RemoteMachineSettings settings)
    {
        SshHostKeyValidator? hostKeyValidator = null;

        try
        {
            using var client = CreateClient(settings);

            hostKeyValidator = new SshHostKeyValidator(settings.HostKeyFingerprint);
            hostKeyValidator.Attach(client);

            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
            client.Connect();

            using var command = client.RunCommand("uname -m");
            var architecture = command.Result.Trim();

            if (command.ExitStatus != 0 || string.IsNullOrWhiteSpace(architecture))
            {
                return RemoteConnectionCheckResult.Failure(localizationService.Get(
                    "SSH подключён, но определить архитектуру машины не удалось."));
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
            return RemoteConnectionCheckResult.Failure(localizationService.Get(
                "SSH-сервер отклонил указанные данные для входа."));
        }
        catch (SshConnectionException exception)
        {
            return RemoteConnectionCheckResult.Failure(
                localizationService.Format(
                    "Не удалось установить SSH-соединение: {0}",
                    exception.Message));
        }
        catch (SocketException exception)
        {
            return RemoteConnectionCheckResult.Failure(
                localizationService.Format(
                    "Удалённая машина недоступна: {0}",
                    exception.Message));
        }
        catch (FileNotFoundException)
        {
            return RemoteConnectionCheckResult.Failure(localizationService.Get(
                "Файл приватного SSH-ключа не найден."));
        }
        catch (UnauthorizedAccessException)
        {
            return RemoteConnectionCheckResult.Failure(localizationService.Get(
                "Нет доступа к файлу приватного SSH-ключа."));
        }
        catch (Exception exception)
        {
            return RemoteConnectionCheckResult.Failure(
                localizationService.Format(
                    "Проверка подключения завершилась ошибкой: {0}",
                    exception.Message));
        }
    }

    private static SshClient CreateClient(RemoteMachineSettings settings)
    {
        if (settings.AuthenticationMethod == PasswordAuthentication)
        {
            return new SshClient(
                settings.Host!,
                settings.Port,
                settings.UserName!,
                settings.Password!);
        }

        var keyFile = new PrivateKeyFile(settings.PrivateKeyPath!);

        return new SshClient(
            settings.Host!,
            settings.Port,
            settings.UserName!,
            keyFile);
    }
}
