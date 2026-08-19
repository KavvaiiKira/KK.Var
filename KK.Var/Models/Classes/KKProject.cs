using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using KK.Var.Enums;

namespace KK.Var.Models;

public sealed class KKProject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [NotMapped]
    public string EffectiveDescription =>
        string.IsNullOrWhiteSpace(Description) ? Name : Description;

    [NotMapped]
    public string LastDeploymentDisplay
    {
        get
        {
            var deployment = Deployments.MaxBy(item => item.StartedAtUtc);

            return deployment is null
                ? "Не выполнялся"
                : deployment.StartedAtUtc.ToLocalTime().ToString("dd.MM.yyyy, HH:mm");
        }
    }

    [NotMapped]
    public string LatestVersionTag =>
        Versions.MaxBy(version => version.CreatedAtUtc)?.Tag ?? "Нет версий";

    [NotMapped]
    public string RemoteExecutablePath =>
        $"{RemoteDeploymentDirectory.TrimEnd('/')}/{RemoteExecutableFileName.TrimStart('/')}";

    [NotMapped]
    public string SourceDisplay => SourceType == ProjectSourceType.GitHubRepository
        ? $"GitHub · {GitHubRepositoryFullName}"
        : $"Локальная папка · {LocalDirectoryPath}";

    public ProjectSourceType SourceType { get; set; }

    public string? LocalDirectoryPath { get; set; }

    public long? GitHubRepositoryId { get; set; }

    public string? GitHubRepositoryFullName { get; set; }

    public string? GitHubCloneUrl { get; set; }

    public ProjectBuildProvider BuildProvider { get; set; }

    public string BuildConfigurationJson { get; set; } = "{}";

    public string RemoteServiceName { get; set; } = string.Empty;

    public string RemoteExecutableFileName { get; set; } = string.Empty;

    public string RemoteDeploymentDirectory { get; set; } = string.Empty;

    public string ProjectEnvironmentFilePath { get; set; } = string.Empty;

    public EnvironmentFileFormat EnvironmentFileFormat { get; set; } =
        EnvironmentFileFormat.Json;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<KKProjectEnvironmentVariable> EnvironmentVariables { get; set; }
        = new List<KKProjectEnvironmentVariable>();

    public ICollection<KKProjectVersion> Versions { get; set; }
        = new List<KKProjectVersion>();

    public ICollection<KKProjectDeployment> Deployments { get; set; }
        = new List<KKProjectDeployment>();
}
