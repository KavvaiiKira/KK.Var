using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectService(IKKProjectRepository repository)
    : IKKProjectService
{
    private static readonly Regex ServiceNamePattern = new(
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
        project.CreatedAtUtc = DateTimeOffset.UtcNow;
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

        var existing = await repository.GetByIdAsync(project.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{project.Id}' was not found.");

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
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(project, cancellationToken);
    }

    public Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(id, cancellationToken);

    private static void NormalizeAndValidate(KKProject project)
    {
        project.Name = Required(project.Name, nameof(project.Name), 200);
        project.Description = Optional(project.Description, 2000);
        project.LocalDirectoryPath = Optional(project.LocalDirectoryPath, 1024);
        project.GitHubRepositoryFullName = Optional(project.GitHubRepositoryFullName, 300);
        project.GitHubCloneUrl = Optional(project.GitHubCloneUrl, 2048);
        project.RemoteDeploymentDirectory = Required(
            project.RemoteDeploymentDirectory,
            nameof(project.RemoteDeploymentDirectory),
            1024);
        project.ProjectEnvironmentFilePath = Required(
            project.ProjectEnvironmentFilePath,
            nameof(project.ProjectEnvironmentFilePath),
            1024);

        project.RemoteServiceName = Required(
            project.RemoteServiceName,
            nameof(project.RemoteServiceName),
            255);

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

        project.BuildConfigurationJson = string.IsNullOrWhiteSpace(project.BuildConfigurationJson)
            ? "{}"
            : project.BuildConfigurationJson.Trim();

        using (var document = JsonDocument.Parse(project.BuildConfigurationJson))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Build configuration must be a JSON object.");
            }
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

        if (normalized?.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static void ValidateLinuxAbsolutePath(string path, string parameterName)
    {
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("An absolute Linux path is required.", parameterName);
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                throw new ArgumentException(
                    "Linux path cannot contain '.' or '..' segments.",
                    parameterName);
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
