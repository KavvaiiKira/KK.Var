using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class ProjectConfigurationTransferService(
    IKKProjectService projectService,
    IKKProjectEnvironmentService environmentService,
    IKKProjectRepository projectRepository,
    ILocalizationService localizationService) : IProjectConfigurationTransferService
{
    private const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<string> ExportAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projectService.GetByIdAsync(projectId, cancellationToken) ??
            throw new InvalidOperationException(localizationService.Get("Проект не найден."));
        var variables = await environmentService.GetAsync(projectId, cancellationToken);
        var data = new ProjectConfigurationData
        {
            Name = project.Name,
            Description = project.Description,
            SourceType = project.SourceType,
            LocalDirectoryPath = project.LocalDirectoryPath,
            GitHubRepositoryId = project.GitHubRepositoryId,
            GitHubRepositoryFullName = project.GitHubRepositoryFullName,
            GitHubCloneUrl = project.GitHubCloneUrl,
            GitHubBranch = project.GitHubBranch,
            BuildProvider = project.BuildProvider,
            RemoteServiceName = project.RemoteServiceName,
            RemoteExecutableFileName = project.RemoteExecutableFileName,
            RemoteDeploymentDirectory = project.RemoteDeploymentDirectory,
            ProjectEnvironmentFilePath = project.ProjectEnvironmentFilePath,
            EnvironmentFileFormat = project.EnvironmentFileFormat,
            HealthCheckType = project.HealthCheckType,
            HealthCheckTimeoutSeconds = project.HealthCheckTimeoutSeconds,
            HealthCheckIntervalSeconds = project.HealthCheckIntervalSeconds,
            HealthCheckAttempts = project.HealthCheckAttempts,
            HealthCheckStabilityDelaySeconds = project.HealthCheckStabilityDelaySeconds,
            HealthCheckHttpUrl = project.HealthCheckHttpUrl,
            HealthCheckTcpPort = project.HealthCheckTcpPort,
            HealthCheckCommand = project.HealthCheckCommand,
            MaxStoredVersions = project.MaxStoredVersions,
            MaxStoredVersionBytes = project.MaxStoredVersionBytes,
            BuildConfiguration = JsonSerializer.Deserialize<ProjectBuildConfiguration>(
                project.BuildConfigurationJson, JsonOptions) ?? throw InvalidDocument(),
            Variables = variables.Select(pair => new ProjectConfigurationVariable(pair.Key, pair.Value)).ToList(),
        };
        return JsonSerializer.Serialize(new ProjectConfigurationDocument
        {
            FormatVersion = CurrentFormatVersion,
            Project = data,
        }, JsonOptions) + Environment.NewLine;
    }

    public ProjectConfigurationDocument Read(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidDocument();
            }
            var version = parsed.RootElement.EnumerateObject()
                .FirstOrDefault(property => property.Name.Equals("formatVersion", StringComparison.OrdinalIgnoreCase)).Value;
            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber))
            {
                throw InvalidDocument();
            }
            if (versionNumber != CurrentFormatVersion)
            {
                throw UnsupportedVersion(versionNumber);
            }
            var document = JsonSerializer.Deserialize<ProjectConfigurationDocument>(json, JsonOptions) ??
                throw InvalidDocument();
            ValidateDocument(document);
            return document;
        }
        catch (JsonException)
        {
            throw InvalidDocument();
        }
    }

    public string Describe(ProjectConfigurationDocument document) =>
        JsonSerializer.Serialize(document, JsonOptions);

    public async Task<KKProject> ImportAsync(ProjectConfigurationDocument document, Guid remoteMachineId,
        CancellationToken cancellationToken = default)
    {
        ValidateDocument(document);
        var data = document.Project!;
        if (await projectRepository.NameExistsAsync(data.Name.Trim(), cancellationToken: cancellationToken))
        {
            throw new InvalidDataException(localizationService.Format(
                "Проект «{0}» уже существует. Измените имя перед импортом.", data.Name.Trim()));
        }
        if (await projectRepository.ServiceNameExistsAsync(data.RemoteServiceName.Trim(), cancellationToken: cancellationToken,
                remoteMachineId: remoteMachineId))
        {
            throw new InvalidDataException(localizationService.Format(
                "Сервис «{0}» уже назначен другому проекту. Измените сервис перед импортом.", data.RemoteServiceName.Trim()));
        }
        var project = new KKProject
        {
            RemoteMachineId = remoteMachineId,
            Name = data.Name,
            Description = data.Description,
            SourceType = data.SourceType,
            LocalDirectoryPath = data.LocalDirectoryPath,
            GitHubRepositoryId = data.GitHubRepositoryId,
            GitHubRepositoryFullName = data.GitHubRepositoryFullName,
            GitHubCloneUrl = data.GitHubCloneUrl,
            GitHubBranch = data.GitHubBranch,
            BuildProvider = data.BuildProvider,
            RemoteServiceName = data.RemoteServiceName,
            RemoteExecutableFileName = data.RemoteExecutableFileName,
            RemoteDeploymentDirectory = data.RemoteDeploymentDirectory,
            ProjectEnvironmentFilePath = data.ProjectEnvironmentFilePath,
            EnvironmentFileFormat = data.EnvironmentFileFormat,
            HealthCheckType = data.HealthCheckType,
            HealthCheckTimeoutSeconds = data.HealthCheckTimeoutSeconds,
            HealthCheckIntervalSeconds = data.HealthCheckIntervalSeconds,
            HealthCheckAttempts = data.HealthCheckAttempts,
            HealthCheckStabilityDelaySeconds = data.HealthCheckStabilityDelaySeconds,
            HealthCheckHttpUrl = data.HealthCheckHttpUrl,
            HealthCheckTcpPort = data.HealthCheckTcpPort,
            HealthCheckCommand = data.HealthCheckCommand,
            MaxStoredVersions = data.MaxStoredVersions,
            MaxStoredVersionBytes = data.MaxStoredVersionBytes,
            BuildConfigurationJson = JsonSerializer.Serialize(data.BuildConfiguration, JsonOptions),
        };
        project.EnvironmentVariables = environmentService.PrepareVariables(project.Id,
            data.Variables.Select(variable => new KeyValuePair<string, string>(variable.Name, variable.Value)).ToArray());
        return await projectService.CreateAsync(project, cancellationToken);
    }

    private void ValidateDocument(ProjectConfigurationDocument document)
    {
        if (document.FormatVersion != CurrentFormatVersion)
        {
            throw UnsupportedVersion(document.FormatVersion);
        }
        if (document.Project is null || document.Project.Name is null ||
            document.Project.RemoteServiceName is null || document.Project.RemoteExecutableFileName is null ||
            document.Project.RemoteDeploymentDirectory is null || document.Project.ProjectEnvironmentFilePath is null ||
            document.Project.BuildConfiguration is null ||
            document.Project.Variables is null || document.Project.Variables.Any(variable =>
                variable is null || variable.Name is null || variable.Value is null))
        {
            throw InvalidDocument();
        }
    }

    private InvalidDataException InvalidDocument() =>
        new InvalidDataException(localizationService.Get("Файл конфигурации проекта некорректен."));

    private InvalidDataException UnsupportedVersion(int version) =>
        new InvalidDataException(localizationService.Format("Версия формата конфигурации {0} не поддерживается.", version));
}
