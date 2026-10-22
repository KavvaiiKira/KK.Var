using System;
using System.IO;
using System.Threading;
using KK.Var.Enums;
using KK.Var.Models;
using Renci.SshNet;
using static KK.Var.Services.RemoteShell;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteHealthCheckService(
        IRemoteCommandRunner commandRunner,
        ILocalizationService localizationService) : IRemoteHealthCheckService
    {
        private const string CheckCurlCommand = "command -v curl >/dev/null && ";

        private const string CheckTcpToolsCommand = "command -v timeout >/dev/null && command -v bash >/dev/null && ";

        private const string CheckTimeoutCommand = "command -v timeout >/dev/null && ";

        public void RunHealthCheck(
            SshClient ssh,
            KKProject project,
            string userName,
            string target,
            TextWriter log,
            IProgress<DeploymentProgress>? progress,
            CancellationToken cancellationToken)
        {
            log.WriteLine(localizationService.Format(
                "Запущен health-check типа {0}.",
                project.HealthCheckType));

            if (project.HealthCheckType == ProjectHealthCheckType.StabilityDelay)
            {
                RunStabilityHealthCheck(ssh, project, log, cancellationToken);
                return;
            }

            RunRetryHealthCheck(
                ssh,
                project,
                userName,
                target,
                log,
                progress,
                cancellationToken);
        }

        private void RunStabilityHealthCheck(
            SshClient ssh,
            KKProject project,
            TextWriter log,
            CancellationToken cancellationToken)
        {
            var delaySeconds = project.HealthCheckStabilityDelaySeconds ??
                throw new InvalidOperationException(localizationService.Get(
                    "Настройки health-check проекта неполны."));
            var initialRestartCount = commandRunner.RunCheckedWithOutput(
                ssh,
                $"sudo systemctl show --property=NRestarts --value {Q(project.RemoteServiceName)}",
                log);
            var deadline = DateTime.UtcNow.AddSeconds(delaySeconds);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = deadline - DateTime.UtcNow;
                WaitForDelay(
                    remaining < TimeSpan.FromSeconds(1) ?
                        remaining :
                        TimeSpan.FromSeconds(1),
                    cancellationToken);

                var activeResult = commandRunner.Run(
                    ssh,
                    $"sudo systemctl is-active --quiet {Q(project.RemoteServiceName)}",
                    log);

                if (activeResult.ExitStatus != 0)
                {
                    throw new HealthCheckFailedException(localizationService.Format(
                        "Сервис не оставался активным в течение {0} секунд.",
                        delaySeconds));
                }
            }

            var finalRestartCount = commandRunner.RunCheckedWithOutput(
                ssh,
                $"sudo systemctl show --property=NRestarts --value {Q(project.RemoteServiceName)}",
                log);

            if (!string.Equals(
                initialRestartCount,
                finalRestartCount,
                StringComparison.Ordinal))
            {
                throw new HealthCheckFailedException(localizationService.Format(
                    "Сервис не оставался активным в течение {0} секунд.",
                    delaySeconds));
            }
        }

        private void RunRetryHealthCheck(
            SshClient ssh,
            KKProject project,
            string userName,
            string target,
            TextWriter log,
            IProgress<DeploymentProgress>? progress,
            CancellationToken cancellationToken)
        {
            var timeoutSeconds = project.HealthCheckTimeoutSeconds ??
                throw new InvalidOperationException(localizationService.Get(
                    "Настройки health-check проекта неполны."));
            var intervalSeconds = project.HealthCheckIntervalSeconds ??
                throw new InvalidOperationException(localizationService.Get(
                    "Настройки health-check проекта неполны."));
            var attempts = project.HealthCheckAttempts ??
                throw new InvalidOperationException(localizationService.Get(
                    "Настройки health-check проекта неполны."));
            var command = CreateHealthCheckCommand(
                project,
                userName,
                target,
                timeoutSeconds);

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attemptMessage = localizationService.Format(
                    "Health-check: попытка {0} из {1}",
                    attempt,
                    attempts);
                progress?.Report(new DeploymentProgress(92, attemptMessage));
                log.WriteLine(attemptMessage);

                try
                {
                    var result = commandRunner.Run(
                        ssh,
                        command,
                        log,
                        TimeSpan.FromSeconds(timeoutSeconds + 5));

                    cancellationToken.ThrowIfCancellationRequested();

                    if (result.ExitStatus == 0)
                    {
                        log.WriteLine(localizationService.Get("Health-check пройден."));
                        return;
                    }

                    log.WriteLine(localizationService.Format(
                        "Попытка health-check завершилась с кодом {0}.",
                        result.ExitStatus));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    log.WriteLine(localizationService.Format(
                        "Попытка health-check завершилась ошибкой: {0}",
                        exception));
                }

                if (attempt < attempts)
                {
                    WaitForDelay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
                }
            }

            throw new HealthCheckFailedException(GetHealthCheckFailureMessage(
                project.HealthCheckType,
                attempts));
        }

        private string CreateHealthCheckCommand(
            KKProject project,
            string userName,
            string target,
            int timeoutSeconds) =>
            project.HealthCheckType switch
            {
                ProjectHealthCheckType.Http =>
                    CheckCurlCommand +
                    $"curl --fail --silent --show-error --location --output /dev/null --max-time {timeoutSeconds} " +
                    Q(project.HealthCheckHttpUrl ?? string.Empty),
                ProjectHealthCheckType.Tcp =>
                    CheckTcpToolsCommand +
                    $"timeout {timeoutSeconds}s bash -c " +
                    Q($"exec 3<>/dev/tcp/127.0.0.1/{project.HealthCheckTcpPort}"),
                ProjectHealthCheckType.Command =>
                    CheckTimeoutCommand +
                    $"sudo -u {Q(userName)} timeout {timeoutSeconds}s sh -lc " +
                    Q($"cd -- {Q(target)} && {project.HealthCheckCommand}"),
                _ => throw new InvalidOperationException(localizationService.Get(
                    "Выбран неподдерживаемый тип health-check.")),
            };

        private string GetHealthCheckFailureMessage(
            ProjectHealthCheckType healthCheckType,
            int attempts) =>
            healthCheckType switch
            {
                ProjectHealthCheckType.Http => localizationService.Format(
                    "HTTP health-check не пройден после {0} попыток.",
                    attempts),
                ProjectHealthCheckType.Tcp => localizationService.Format(
                    "TCP health-check не пройден после {0} попыток.",
                    attempts),
                ProjectHealthCheckType.Command => localizationService.Format(
                    "Командный health-check не пройден после {0} попыток.",
                    attempts),
                _ => localizationService.Get("Health-check не пройден."),
            };

        private static void WaitForDelay(
            TimeSpan delay,
            CancellationToken cancellationToken)
        {
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            if (cancellationToken.WaitHandle.WaitOne(delay))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private sealed class HealthCheckFailedException : InvalidOperationException
        {
            public HealthCheckFailedException(string message)
                : base(message)
            {
            }
        }
    }
}
