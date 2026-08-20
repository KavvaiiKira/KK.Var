using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectService(
    IKKProjectRepository repository,
    IProjectArtifactService artifactService,
    ILocalizationService localizationService)
    : IKKProjectService
{
    private static readonly Regex ServiceNamePattern = new Regex(
        "^[A-Za-z0-9_.@-]+\\.service$",
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

        if (await repository.NameExistsAsync(project.Name, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"A project named '{project.Name}' already exists.");
        }

        if (await repository.ServiceNameExistsAsync(
                project.RemoteServiceName,
                cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException(
                $"Systemd service '{project.RemoteServiceName}' is already assigned to another project.");
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
            throw new ArgumentException("Project id is required.", nameof(project));
        }

        var existing = await repository.GetByIdAsync(project.Id, cancellationToken) ??
            throw new KeyNotFoundException($"Project '{project.Id}' was not found.");

        NormalizeAndValidate(project);

        if (await repository.NameExistsAsync(
                project.Name,
                project.Id,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"A project named '{project.Name}' already exists.");
        }

        if (await repository.ServiceNameExistsAsync(
                project.RemoteServiceName,
                project.Id,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Systemd service '{project.RemoteServiceName}' is already assigned to another project.");
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

        if (!project.RemoteServiceName.EndsWith(".service", StringComparison.OrdinalIgnoreCase))
        {
            project.RemoteServiceName += ".service";
        }

        if (!ServiceNamePattern.IsMatch(project.RemoteServiceName))
        {
            throw new ArgumentException("Invalid systemd service name.");
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
                "Unsupported environment file format.");
        }

        project.BuildConfigurationJson =
            string.IsNullOrWhiteSpace(project.BuildConfigurationJson) ?
                "{}" :
                project.BuildConfigurationJson.Trim();

        using (var document = JsonDocument.Parse(project.BuildConfigurationJson))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Build configuration must be a JSON object.");
            }
        }

        var buildConfiguration = JsonSerializer.Deserialize<ProjectBuildConfiguration>(
            project.BuildConfigurationJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                throw new ArgumentException("Build configuration is invalid.");

        if (buildConfiguration.ConfigureArguments is null ||
            buildConfiguration.BuildArguments is null ||
            buildConfiguration.Environment is null ||
            buildConfiguration.ConfigureArguments.Any(string.IsNullOrWhiteSpace) ||
            buildConfiguration.BuildArguments.Any(string.IsNullOrWhiteSpace) ||
            buildConfiguration.Environment.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Build arguments and environment names must not be empty.");
        }

        if (project.BuildProvider == ProjectBuildProvider.Custom &&
            string.IsNullOrWhiteSpace(buildConfiguration.Command))
        {
            throw new ArgumentException("Custom build command is required.");
        }

        if (project.BuildProvider == ProjectBuildProvider.Cpp &&
            string.IsNullOrWhiteSpace(buildConfiguration.ToolchainFile))
        {
            throw new ArgumentException("C++ Linux toolchain file is required.");
        }

        switch (project.SourceType)
        {
            case ProjectSourceType.LocalDirectory:
                if (project.LocalDirectoryPath is null)
                {
                    throw new ArgumentException("Local directory path is required.");
                }

                project.GitHubRepositoryId = null;
                project.GitHubRepositoryFullName = null;
                project.GitHubCloneUrl = null;

                break;

            case ProjectSourceType.GitHubRepository:
                if (project.GitHubRepositoryId is null ||
                    project.GitHubRepositoryFullName is null ||
                    project.GitHubCloneUrl is null)
                {
                    throw new ArgumentException(
                        "GitHub repository id, full name and clone URL are required.");
                }

                project.LocalDirectoryPath = null;

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(project.SourceType),
                    project.SourceType,
                    "Unsupported project source type.");
        }
    }

    private static string Required(string? value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maxLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        return normalized?.Length > maxLength ?
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.") :
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

        if (segments[0].Equals("home", StringComparison.OrdinalIgnoreCase) &&
            segments.Length < 3)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя разворачивать проект прямо в домашнюю директорию пользователя. Укажите вложенную директорию приложения."));
        }

        var restrictedPrefixes = new[]
        {
            "/bin/",
            "/boot/",
            "/dev/",
            "/etc/",
            "/lib/",
            "/lib64/",
            "/proc/",
            "/run/",
            "/sbin/",
            "/sys/",
            "/usr/bin/",
            "/usr/lib/",
            "/usr/lib64/",
            "/usr/sbin/",
            "/usr/share/",
        };

        var normalized = path + "/";

        if (restrictedPrefixes.Any(prefix =>
                normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
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

    private static string ValidateProjectRelativePath(string path, string parameterName)
    {
        var normalized = path.Replace('\\', '/');

        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            normalized.EndsWith("/", StringComparison.Ordinal) ||
            normalized.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "A relative path to a file inside the project is required.",
                parameterName);
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
            {
                throw new ArgumentException(
                    "Project file path contains an invalid segment.",
                    parameterName);
            }
        }

        return normalized;
    }
}
