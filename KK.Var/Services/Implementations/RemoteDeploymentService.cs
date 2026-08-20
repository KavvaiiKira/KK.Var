using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;
using Renci.SshNet;

namespace KK.Var.Services.Implementations;

public sealed class RemoteDeploymentService(ILocalizationService localizationService)
    : IRemoteDeploymentService
{
    private const string PasswordAuthentication = "Пароль";

    public Task DeployAsync(
        KKProject project,
        string artifactPath,
        RemoteMachineSettings settings,
        TextWriter log,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default) => Task.Run(
        () => Deploy(project, artifactPath, settings, log, progress, cancellationToken),
        cancellationToken);

    private void Deploy(
        KKProject project,
        string artifactPath,
        RemoteMachineSettings settings,
        TextWriter log,
        IProgress<DeploymentProgress>? progress,
        CancellationToken cancellationToken)
    {
        Validate(settings, project, artifactPath);
        var operationId = Guid.NewGuid().ToString("N");
        var remoteArchive = $"/tmp/kk-var-{operationId}.tar.gz";
        var remoteUnit = $"/tmp/kk-var-{operationId}.service";
        var target = project.RemoteDeploymentDirectory.TrimEnd('/');
        var staging = $"{target}.staging-{operationId}";
        var backup = $"{target}.previous-{operationId}";
        var unitPath = $"/etc/systemd/system/{project.RemoteServiceName}";
        var unitBackup = $"/tmp/kk-var-{operationId}.service.backup";
        var unitContent = CreateUnit(project, settings.UserName!);
        var unitState = UnitState.Unchanged;
        var targetMoved = false;

        using var ssh = CreateSshClient(settings);
        using var sftp = CreateSftpClient(settings);
        ssh.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
        sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
        ssh.Connect();
        sftp.Connect();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DeploymentProgress(
                55,
                localizationService.Get("Проверка удалённой машины")));
            RunChecked(ssh, "sudo -n true", log);
            RunChecked(ssh, "command -v systemctl >/dev/null && command -v tar >/dev/null", log);
            var actualArchitecture = RunCheckedWithOutput(ssh, "uname -m", log);
            if (!string.Equals(
                    actualArchitecture,
                    settings.Architecture,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(localizationService.Format(
                    "Архитектура удалённой машины изменилась: ожидалась {0}, получена {1}. Выполните проверку подключения ещё раз.",
                    settings.Architecture,
                    actualArchitecture));
            }
            var requiredKilobytes = Math.Max(1024, new FileInfo(artifactPath).Length / 1024 * 3);
            RunChecked(
                ssh,
                $"available=$(df -Pk -- \"$(dirname -- {Q(target)})\" | awk 'NR==2 {{print $4}}'); " +
                $"test -n \"$available\" && test \"$available\" -ge {requiredKilobytes}",
                log);
            progress?.Report(new DeploymentProgress(
                58,
                localizationService.Get("Удалённая машина готова к Deploy")));

            progress?.Report(new DeploymentProgress(
                60,
                localizationService.Get("Загрузка архива на удалённую машину")));
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
                localizationService.Get("Распаковка новой версии")));
            RunChecked(
                ssh,
                $"sudo rm -rf -- {Q(staging)} && sudo mkdir -p -- {Q(staging)} && " +
                $"sudo tar -xzf {Q(remoteArchive)} -C {Q(staging)} && " +
                $"sudo chown -R {Q(settings.UserName!)} -- {Q(staging)}",
                log);

            if (IsPythonProject(project))
            {
                RunChecked(ssh, "command -v python3 >/dev/null", log);
                RunChecked(
                    ssh,
                    $"sudo -u {Q(settings.UserName!)} python3 -m venv {Q(staging + "/.venv")} && " +
                    $"if test -f {Q(staging + "/requirements.txt")}; then " +
                    $"sudo -u {Q(settings.UserName!)} {Q(staging + "/.venv/bin/pip")} install -r {Q(staging + "/requirements.txt")}; " +
                    $"elif test -f {Q(staging + "/pyproject.toml")}; then " +
                    $"sudo -u {Q(settings.UserName!)} {Q(staging + "/.venv/bin/pip")} install {Q(staging)}; fi",
                    log);
            }
            else
            {
                RunChecked(
                    ssh,
                    $"sudo chmod u+x -- {Q(staging + "/" + project.RemoteExecutableFileName)}",
                    log);
            }

            var unitExists = Run(ssh, $"sudo test -f {Q(unitPath)}", log).ExitStatus == 0;
            if (!unitExists)
            {
                unitState = UnitState.Created;
                progress?.Report(new DeploymentProgress(
                    70,
                    localizationService.Get("Systemd unit будет создан")));
            }
            else if (Run(ssh, $"sudo cmp -s -- {Q(remoteUnit)} {Q(unitPath)}", log).ExitStatus != 0)
            {
                unitState = UnitState.Changed;
                RunChecked(ssh, $"sudo cp -- {Q(unitPath)} {Q(unitBackup)}", log);
                progress?.Report(new DeploymentProgress(
                    70,
                    localizationService.Get("Systemd unit будет обновлён")));
            }
            else
            {
                progress?.Report(new DeploymentProgress(
                    70,
                    localizationService.Get("Systemd unit не изменился")));
            }

            progress?.Report(new DeploymentProgress(
                75,
                localizationService.Get("Переключение версии")));
            Run(ssh, $"sudo systemctl stop {Q(project.RemoteServiceName)}", log);
            RunChecked(
                ssh,
                $"if sudo test -e {Q(target)}; then sudo mv -- {Q(target)} {Q(backup)}; fi && " +
                $"sudo mv -- {Q(staging)} {Q(target)}",
                log);
            targetMoved = true;

            if (unitState != UnitState.Unchanged)
            {
                RunChecked(ssh, $"sudo systemd-analyze verify {Q(remoteUnit)}", log);
                RunChecked(
                    ssh,
                    $"sudo install -o root -g root -m 0644 {Q(remoteUnit)} {Q(unitPath)} && sudo systemctl daemon-reload",
                    log);
                progress?.Report(new DeploymentProgress(
                    82,
                    localizationService.Get("Systemd перечитал изменённый unit")));
            }

            if (Run(ssh, $"sudo systemctl is-enabled {Q(project.RemoteServiceName)}", log).ExitStatus != 0)
            {
                RunChecked(ssh, $"sudo systemctl enable {Q(project.RemoteServiceName)}", log);
            }

            progress?.Report(new DeploymentProgress(
                88,
                localizationService.Get("Запуск systemd-сервиса")));
            RunChecked(ssh, $"sudo systemctl restart {Q(project.RemoteServiceName)}", log);
            RunChecked(ssh, $"sudo systemctl is-active --quiet {Q(project.RemoteServiceName)}", log);
            progress?.Report(new DeploymentProgress(
                96,
                localizationService.Get("Systemd-сервис успешно запущен")));
            RunChecked(ssh, $"sudo rm -rf -- {Q(backup)} {Q(remoteArchive)} {Q(remoteUnit)} {Q(unitBackup)}", log);
            progress?.Report(new DeploymentProgress(
                100,
                localizationService.Get("Deploy завершён")));
        }
        catch
        {
            progress?.Report(new DeploymentProgress(
                0,
                localizationService.Get(
                    "Ошибка Deploy, восстановление предыдущей версии")));
            try
            {
                if (targetMoved)
                {
                    Run(
                        ssh,
                        $"sudo systemctl stop {Q(project.RemoteServiceName)}; " +
                        $"sudo rm -rf -- {Q(target)}; " +
                        $"if sudo test -e {Q(backup)}; then sudo mv -- {Q(backup)} {Q(target)}; fi",
                        log);
                }

                if (unitState == UnitState.Created)
                {
                    Run(ssh, $"sudo rm -f -- {Q(unitPath)} && sudo systemctl daemon-reload", log);
                }
                else if (unitState == UnitState.Changed)
                {
                    Run(
                        ssh,
                        $"sudo install -o root -g root -m 0644 {Q(unitBackup)} {Q(unitPath)} && sudo systemctl daemon-reload",
                        log);
                }

                if (targetMoved && Run(ssh, $"sudo test -e {Q(target)}", log).ExitStatus == 0)
                {
                    Run(ssh, $"sudo systemctl restart {Q(project.RemoteServiceName)}", log);
                    progress?.Report(new DeploymentProgress(
                        0,
                        localizationService.Get("Предыдущая версия восстановлена")));
                }
            }
            finally
            {
                Run(
                    ssh,
                    $"sudo rm -rf -- {Q(staging)} {Q(remoteArchive)} {Q(remoteUnit)} {Q(unitBackup)}",
                    log);
            }
            throw;
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

    private static SshCommand Run(SshClient client, string command, TextWriter log)
    {
        log.WriteLine($"> {command}");
        var result = client.RunCommand(command);
        if (!string.IsNullOrWhiteSpace(result.Result))
        {
            log.WriteLine(result.Result.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            log.WriteLine(result.Error.TrimEnd());
        }
        return result;
    }

    private void RunChecked(SshClient client, string command, TextWriter log)
    {
        using var result = Run(client, command, log);
        if (result.ExitStatus != 0)
        {
            var message = string.IsNullOrWhiteSpace(result.Error)
                ? localizationService.Format(
                    "Удалённая команда завершилась с кодом {0}.",
                    result.ExitStatus)
                : result.Error.Trim();
            throw new InvalidOperationException(message);
        }
    }

    private string RunCheckedWithOutput(
        SshClient client,
        string command,
        TextWriter log)
    {
        using var result = Run(client, command, log);
        if (result.ExitStatus != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Error)
                    ? localizationService.Format(
                        "Удалённая команда завершилась с кодом {0}.",
                        result.ExitStatus)
                    : result.Error.Trim());
        }
        return result.Result.Trim();
    }

    private string CreateUnit(KKProject project, string userName)
    {
        RejectControlCharacters(userName);
        var directory = project.RemoteDeploymentDirectory.TrimEnd('/');
        var executable = $"{directory}/{project.RemoteExecutableFileName}";
        var execStart = IsPythonProject(project)
            ? $"{UnitQuote(directory + "/.venv/bin/python")} {UnitQuote(executable)}"
            : UnitQuote(executable);
        return string.Join('\n',
        [
            "[Unit]",
            $"Description={project.EffectiveDescription.Replace("\r", " ").Replace("\n", " ")}",
            "After=network.target",
            "",
            "[Service]",
            "Type=simple",
            $"User={userName}",
            $"WorkingDirectory={UnitPath(directory)}",
            $"ExecStart={execStart}",
            "Restart=on-failure",
            "RestartSec=5",
            "",
            "[Install]",
            "WantedBy=multi-user.target",
            "",
        ]);
    }

    private void Validate(
        RemoteMachineSettings settings,
        KKProject project,
        string artifactPath)
    {
        if (!File.Exists(artifactPath))
        {
            throw new FileNotFoundException(
                localizationService.Get("Локальный архив версии не найден."),
                artifactPath);
        }
        if (string.IsNullOrWhiteSpace(settings.Host) ||
            string.IsNullOrWhiteSpace(settings.UserName))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Удалённая машина не настроена."));
        }
        RejectControlCharacters(project.RemoteServiceName);
        RejectControlCharacters(project.RemoteDeploymentDirectory);
        RejectControlCharacters(project.RemoteExecutableFileName);
    }

    private void RejectControlCharacters(string value)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Параметр содержит недопустимые символы."));
        }
    }

    private static string Q(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private static string UnitQuote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static string UnitPath(string value) => value
        .Replace("\\", "\\\\")
        .Replace(" ", "\\x20")
        .Replace("\t", "\\t")
        .Replace("%", "%%");

    private static bool IsPythonProject(KKProject project) =>
        project.BuildProvider == ProjectBuildProvider.Python ||
        project.RemoteExecutableFileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase);

    private static SshClient CreateSshClient(RemoteMachineSettings settings) =>
        settings.AuthenticationMethod == PasswordAuthentication
            ? new SshClient(settings.Host!, settings.Port, settings.UserName!, settings.Password!)
            : new SshClient(
                settings.Host!,
                settings.Port,
                settings.UserName!,
                new PrivateKeyFile(settings.PrivateKeyPath!));

    private static SftpClient CreateSftpClient(RemoteMachineSettings settings) =>
        settings.AuthenticationMethod == PasswordAuthentication
            ? new SftpClient(settings.Host!, settings.Port, settings.UserName!, settings.Password!)
            : new SftpClient(
                settings.Host!,
                settings.Port,
                settings.UserName!,
                new PrivateKeyFile(settings.PrivateKeyPath!));

    private enum UnitState
    {
        Unchanged,
        Created,
        Changed,
    }
}
