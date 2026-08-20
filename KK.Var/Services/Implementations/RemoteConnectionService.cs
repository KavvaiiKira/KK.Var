using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
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
