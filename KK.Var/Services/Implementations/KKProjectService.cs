using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations
{
    public sealed class KKProjectService(
        IBuildConfigurationService buildConfigurationService,
        IKKProjectRepository repository,
        IProjectArtifactService artifactService,
        IRemoteMachineService machineService,
        ILocalizationService localizationService)
        : IKKProjectService
    {
        private const string ServiceNameValidationPattern = "^[A-Za-z0-9_.@-]+\\.service$";

        private static readonly Regex ServiceNamePattern = new Regex(
            ServiceNameValidationPattern,
            RegexOptions.CultureInvariant);

        public Task<IReadOnlyList<KKProject>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            repository.GetAllAsync(cancellationToken);

        public Task<KKProject?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            repository.GetByIdAsync(id, cancellationToken);

        public async Task<KKProject> CreateAsync(
            KKProject project,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);
            NormalizeAndValidate(project);
            await using var machineReservation = await machineService.ReserveAsync(
                project.RemoteMachineId,
                cancellationToken);

            if (await repository.NameExistsAsync(project.Name, cancellationToken: cancellationToken))
            {
                throw new InvalidOperationException(
                    localizationService.Format("Проект с именем «{0}» уже существует.", project.Name));
            }

            if (await repository.ServiceNameExistsAsync(
                project.RemoteServiceName,
                cancellationToken: cancellationToken,
                remoteMachineId: project.RemoteMachineId))
            {
                throw new InvalidOperationException(
                    localizationService.Format("Systemd-сервис «{0}» уже назначен другому проекту.", project.RemoteServiceName));
            }

            project.Id = project.Id == Guid.Empty ? Guid.NewGuid() : project.Id;
            project.CreatedAtUtc = DateTime.UtcNow;
            project.UpdatedAtUtc = project.CreatedAtUtc;

            await repository.AddAsync(project, cancellationToken);

            return project;
        }

        public async Task UpdateAsync(
            KKProject project,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);

            if (project.Id == Guid.Empty)
            {
                throw new ArgumentException(localizationService.Get("Укажите идентификатор проекта."), nameof(project));
            }

            var existing = await repository.GetByIdAsync(project.Id, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Format("Проект «{0}» не найден.", project.Id));

            NormalizeAndValidate(project);
            await using var machineReservation = await machineService.ReserveAsync(
                project.RemoteMachineId,
                cancellationToken);

            if (await repository.NameExistsAsync(
                project.Name,
                project.Id,
                cancellationToken))
            {
                throw new InvalidOperationException(
                    localizationService.Format("Проект с именем «{0}» уже существует.", project.Name));
            }

            if (await repository.ServiceNameExistsAsync(
                project.RemoteServiceName,
                project.Id,
                cancellationToken,
                remoteMachineId: project.RemoteMachineId))
            {
                throw new InvalidOperationException(
                    localizationService.Format("Systemd-сервис «{0}» уже назначен другому проекту.", project.RemoteServiceName));
            }

            project.CreatedAtUtc = existing.CreatedAtUtc;
            project.UpdatedAtUtc = DateTime.UtcNow;

            await repository.UpdateAsync(project, cancellationToken);
        }

        public async Task DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            await artifactService.DeleteAllAsync(id, cancellationToken);
            await repository.DeleteAsync(id, cancellationToken);
        }

        private void NormalizeAndValidate(KKProject project)
        {
            project.Name = Required(project.Name, nameof(project.Name), 200);
            project.Description = Optional(project.Description, 2000);
            project.LocalDirectoryPath = Optional(project.LocalDirectoryPath, 1024);
            project.GitHubRepositoryFullName = Optional(project.GitHubRepositoryFullName, 300);
            project.GitHubCloneUrl = Optional(project.GitHubCloneUrl, 2048);
            project.GitHubBranch = Optional(project.GitHubBranch, 255);
            if (project.GitHubBranch?.Any(char.IsControl) == true)
            {
                throw new ArgumentException(localizationService.Get("Некорректное имя ветки GitHub."));
            }
            project.RemoteDeploymentDirectory = Required(
                project.RemoteDeploymentDirectory,
                nameof(project.RemoteDeploymentDirectory),
                1024).TrimEnd('/');
            project.ProjectEnvironmentFilePath = Required(
                project.ProjectEnvironmentFilePath,
                nameof(project.ProjectEnvironmentFilePath),
                1024);

            project.RemoteServiceName = Required(
                project.RemoteServiceName,
                nameof(project.RemoteServiceName),
                255);
            project.RemoteExecutableFileName = Required(
                project.RemoteExecutableFileName,
                nameof(project.RemoteExecutableFileName),
                255);
            project.RemoteExecutableFileName = ValidateProjectRelativePath(
                project.RemoteExecutableFileName,
                nameof(project.RemoteExecutableFileName));

            if (!project.RemoteServiceName.EndsWith(DeploymentConstants.ServiceExtension, StringComparison.OrdinalIgnoreCase))
            {
                project.RemoteServiceName += DeploymentConstants.ServiceExtension;
            }

            if (!ServiceNamePattern.IsMatch(project.RemoteServiceName))
            {
                throw new ArgumentException(localizationService.Get("Недопустимое имя systemd-сервиса."));
            }

            ValidateLinuxAbsolutePath(
                project.RemoteDeploymentDirectory,
                nameof(project.RemoteDeploymentDirectory));

            project.ProjectEnvironmentFilePath = ValidateProjectRelativePath(
                project.ProjectEnvironmentFilePath,
                nameof(project.ProjectEnvironmentFilePath));

            if (!Enum.IsDefined(project.EnvironmentFileFormat))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(project.EnvironmentFileFormat),
                    project.EnvironmentFileFormat,
                    localizationService.Get("Неподдерживаемый формат файла окружения."));
            }

            NormalizeAndValidateHealthCheck(project);

            if (project.MaxStoredVersions is < 1 or > 10000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(project.MaxStoredVersions),
                    localizationService.Get(
                                        "Максимальное количество версий должно быть от 1 до 10000."));
            }

            if (project.MaxStoredVersionBytes is <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(project.MaxStoredVersionBytes),
                    localizationService.Get(
                                        "Лимит размера должен быть больше нуля."));
            }

            project.BuildConfigurationJson =
                string.IsNullOrWhiteSpace(project.BuildConfigurationJson) ?
                    "{}" :
                    project.BuildConfigurationJson.Trim();

            var buildConfiguration = buildConfigurationService.ReadAndValidate(
                project.BuildConfigurationJson,
                project.BuildProvider,
                project.SourceType == ProjectSourceType.LocalDirectory ? project.LocalDirectoryPath : null);

            if (buildConfiguration.ConfigureArguments is null ||
                buildConfiguration.BuildArguments is null ||
                buildConfiguration.Environment is null ||
                buildConfiguration.ConfigureArguments.Any(string.IsNullOrWhiteSpace) ||
                buildConfiguration.BuildArguments.Any(string.IsNullOrWhiteSpace) ||
                buildConfiguration.Environment.Keys.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(localizationService.Get("Аргументы сборки и имена переменных окружения не должны быть пустыми."));
            }

            if (project.BuildProvider == ProjectBuildProvider.Custom &&
                string.IsNullOrWhiteSpace(buildConfiguration.Command))
            {
                throw new ArgumentException(localizationService.Get("Укажите команду пользовательской сборки."));
            }

            if (project.BuildProvider == ProjectBuildProvider.Cpp &&
                string.IsNullOrWhiteSpace(buildConfiguration.ToolchainFile))
            {
                throw new ArgumentException(localizationService.Get("Укажите toolchainFile для сборки C++ под Linux."));
            }

            switch (project.SourceType)
            {
                case ProjectSourceType.LocalDirectory:
                    if (project.LocalDirectoryPath is null)
                    {
                        throw new ArgumentException(localizationService.Get("Укажите путь к локальному каталогу."));
                    }

                    project.GitHubRepositoryId = null;
                    project.GitHubRepositoryFullName = null;
                    project.GitHubCloneUrl = null;
                    project.GitHubBranch = null;

                    break;

                case ProjectSourceType.GitHubRepository:
                    if (project.GitHubRepositoryId is null ||
                        project.GitHubRepositoryFullName is null ||
                        project.GitHubCloneUrl is null)
                    {
                        throw new ArgumentException(
                            localizationService.Get("Укажите идентификатор, полное имя и URL клонирования репозитория GitHub."));
                    }

                    project.LocalDirectoryPath = null;

                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(project.SourceType),
                        project.SourceType,
                        localizationService.Get("Неподдерживаемый тип источника проекта."));
            }
        }

        private void NormalizeAndValidateHealthCheck(KKProject project)
        {
            if (!Enum.IsDefined(project.HealthCheckType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(project.HealthCheckType),
                    project.HealthCheckType,
                    localizationService.Get("Выбран неподдерживаемый тип health-check."));
            }

            project.HealthCheckHttpUrl = Optional(project.HealthCheckHttpUrl, 2048);
            project.HealthCheckCommand = Optional(project.HealthCheckCommand, 4000);

            switch (project.HealthCheckType)
            {
                case ProjectHealthCheckType.None:
                    ClearHealthCheckSettings(project);
                    break;

                case ProjectHealthCheckType.StabilityDelay:
                    ValidateRange(
                        project.HealthCheckStabilityDelaySeconds,
                        1,
                        3600,
                        localizationService.Get(
                                                "Задержка стабильности должна быть от 1 до 3600 секунд."));
                    project.HealthCheckTimeoutSeconds = null;
                    project.HealthCheckIntervalSeconds = null;
                    project.HealthCheckAttempts = null;
                    project.HealthCheckHttpUrl = null;
                    project.HealthCheckTcpPort = null;
                    project.HealthCheckCommand = null;
                    break;

                case ProjectHealthCheckType.Http:
                    ValidateRetrySettings(project);

                    if (project.HealthCheckHttpUrl is null ||
                        !Uri.TryCreate(project.HealthCheckHttpUrl, UriKind.Absolute, out var uri) ||
                        (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ArgumentException(localizationService.Get(
                            "Укажите абсолютный HTTP- или HTTPS-адрес health-check."));
                    }

                    project.HealthCheckStabilityDelaySeconds = null;
                    project.HealthCheckTcpPort = null;
                    project.HealthCheckCommand = null;
                    break;

                case ProjectHealthCheckType.Tcp:
                    ValidateRetrySettings(project);
                    ValidateRange(
                        project.HealthCheckTcpPort,
                        1,
                        65535,
                        localizationService.Get("TCP-порт должен быть от 1 до 65535."));
                    project.HealthCheckStabilityDelaySeconds = null;
                    project.HealthCheckHttpUrl = null;
                    project.HealthCheckCommand = null;
                    break;

                case ProjectHealthCheckType.Command:
                    ValidateRetrySettings(project);

                    if (project.HealthCheckCommand is null)
                    {
                        throw new ArgumentException(localizationService.Get(
                            "Укажите команду health-check."));
                    }

                    project.HealthCheckStabilityDelaySeconds = null;
                    project.HealthCheckHttpUrl = null;
                    project.HealthCheckTcpPort = null;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(project.HealthCheckType));
            }
        }

        private void ValidateRetrySettings(KKProject project)
        {
            ValidateRange(
                project.HealthCheckTimeoutSeconds,
                1,
                3600,
                localizationService.Get("Timeout health-check должен быть от 1 до 3600 секунд."));
            ValidateRange(
                project.HealthCheckIntervalSeconds,
                1,
                3600,
                localizationService.Get("Интервал health-check должен быть от 1 до 3600 секунд."));
            ValidateRange(
                project.HealthCheckAttempts,
                1,
                100,
                localizationService.Get("Количество попыток health-check должно быть от 1 до 100."));
        }

        private static void ClearHealthCheckSettings(KKProject project)
        {
            project.HealthCheckTimeoutSeconds = null;
            project.HealthCheckIntervalSeconds = null;
            project.HealthCheckAttempts = null;
            project.HealthCheckStabilityDelaySeconds = null;
            project.HealthCheckHttpUrl = null;
            project.HealthCheckTcpPort = null;
            project.HealthCheckCommand = null;
        }

        private static void ValidateRange(
            int? value,
            int minimum,
            int maximum,
            string errorMessage)
        {
            if (value is null || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), errorMessage);
            }
        }

        private string Required(string? value, string parameterName, int maxLength)
        {
            var normalized = value?.Trim();

            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException(localizationService.Get("Укажите значение."), parameterName);
            }

            if (normalized.Length > maxLength)
            {
                throw new ArgumentException(
                    localizationService.Format("Значение не может превышать {0} символов.", maxLength),
                    parameterName);
            }

            return normalized;
        }

        private string? Optional(string? value, int maxLength)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            return normalized?.Length > maxLength ?
                throw new ArgumentException(localizationService.Format("Значение не может превышать {0} символов.", maxLength)) :
                normalized;
        }

        private void ValidateLinuxAbsolutePath(string path, string parameterName)
        {
            if (!path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf('\0') >= 0)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Укажите абсолютный Linux-путь к отдельной директории приложения."));
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Нельзя разворачивать проект в корневую директорию Linux. Укажите отдельную директорию приложения, например /opt/my-app."));
            }

            if (segments[0].Equals(RemotePaths.HomeDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                segments.Length < 3)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Нельзя разворачивать проект прямо в домашнюю директорию пользователя. Укажите вложенную директорию приложения."));
            }

            if (RemotePaths.IsProtectedDirectory(path))
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Нельзя разворачивать проект в защищённую системную директорию Linux. Выберите отдельную директорию приложения."));
            }

            foreach (var segment in segments)
            {
                if (segment is "." or "..")
                {
                    throw new InvalidOperationException(localizationService.Get(
                        "Путь развёртывания не может содержать сегменты . или ..."));
                }
            }
        }

        private string ValidateProjectRelativePath(string path, string parameterName)
        {
            var normalized = path.Replace('\\', '/');

            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(normalized) ||
                normalized.EndsWith("/", StringComparison.Ordinal) ||
                normalized.IndexOf('\0') >= 0)
            {
                throw new ArgumentException(
                    localizationService.Get("Укажите относительный путь к файлу внутри проекта."),
                    parameterName);
            }

            foreach (var segment in normalized.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
                {
                    throw new ArgumentException(
                        localizationService.Get("Путь к файлу проекта содержит недопустимый сегмент."),
                        parameterName);
                }
            }

            return normalized;
        }
    }
}
