using System.Collections.Generic;
using KK.Var.Enums;

namespace KK.Var.Models;

public sealed class ProjectConfigurationDocument
{
    public int FormatVersion { get; set; }

    public ProjectConfigurationData? Project { get; set; }
}

public sealed class ProjectConfigurationData
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectSourceType SourceType { get; set; }

    public string? LocalDirectoryPath { get; set; }

    public long? GitHubRepositoryId { get; set; }

    public string? GitHubRepositoryFullName { get; set; }

    public string? GitHubCloneUrl { get; set; }

    public string? GitHubBranch { get; set; }

    public ProjectBuildProvider BuildProvider { get; set; }

    public string RemoteServiceName { get; set; } = string.Empty;

    public string RemoteExecutableFileName { get; set; } = string.Empty;

    public string RemoteDeploymentDirectory { get; set; } = string.Empty;

    public string ProjectEnvironmentFilePath { get; set; } = string.Empty;

    public EnvironmentFileFormat EnvironmentFileFormat { get; set; }

    public ProjectHealthCheckType HealthCheckType { get; set; }

    public int? HealthCheckTimeoutSeconds { get; set; }

    public int? HealthCheckIntervalSeconds { get; set; }

    public int? HealthCheckAttempts { get; set; }

    public int? HealthCheckStabilityDelaySeconds { get; set; }

    public string? HealthCheckHttpUrl { get; set; }

    public int? HealthCheckTcpPort { get; set; }

    public string? HealthCheckCommand { get; set; }

    public int MaxStoredVersions { get; set; } = 10;

    public long? MaxStoredVersionBytes { get; set; }

    public ProjectBuildConfiguration BuildConfiguration { get; set; } = new ProjectBuildConfiguration();

    public List<ProjectConfigurationVariable> Variables { get; set; } = [];
}

public sealed record ProjectConfigurationVariable(string Name, string Value);
