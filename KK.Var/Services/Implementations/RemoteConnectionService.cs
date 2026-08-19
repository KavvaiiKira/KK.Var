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

public sealed class RemoteConnectionService : IRemoteConnectionService
{
    private const string PasswordAuthentication = "Пароль";

    public Task<RemoteConnectionCheckResult> CheckAsync(
        RemoteMachineSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Task.Run(() => Check(settings), cancellationToken);
    }

    private static RemoteConnectionCheckResult Check(RemoteMachineSettings settings)
    {
        try
        {
            using var client = CreateClient(settings);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
            client.Connect();

            using var command = client.RunCommand("uname -m");
            var architecture = command.Result.Trim();

            if (command.ExitStatus != 0 || string.IsNullOrWhiteSpace(architecture))
            {
                return RemoteConnectionCheckResult.Failure(
                    "SSH подключён, но определить архитектуру машины не удалось.");
            }

            client.Disconnect();
            return RemoteConnectionCheckResult.Success(architecture);
        }
        catch (SshAuthenticationException)
        {
            return RemoteConnectionCheckResult.Failure(
                "SSH-сервер отклонил указанные данные для входа.");
        }
        catch (SshConnectionException exception)
        {
            return RemoteConnectionCheckResult.Failure(
                $"Не удалось установить SSH-соединение: {exception.Message}");
        }
        catch (SocketException exception)
        {
            return RemoteConnectionCheckResult.Failure(
                $"Удалённая машина недоступна: {exception.Message}");
        }
        catch (FileNotFoundException)
        {
            return RemoteConnectionCheckResult.Failure(
                "Файл приватного SSH-ключа не найден.");
        }
        catch (UnauthorizedAccessException)
        {
            return RemoteConnectionCheckResult.Failure(
                "Нет доступа к файлу приватного SSH-ключа.");
        }
        catch (Exception exception)
        {
            return RemoteConnectionCheckResult.Failure(
                $"Проверка подключения завершилась ошибкой: {exception.Message}");
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
