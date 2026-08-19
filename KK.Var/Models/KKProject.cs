using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
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

    public ProjectSourceType SourceType { get; set; }

    public string? LocalDirectoryPath { get; set; }

    public long? GitHubRepositoryId { get; set; }

    public string? GitHubRepositoryFullName { get; set; }

    public string? GitHubCloneUrl { get; set; }

    public ProjectBuildProvider BuildProvider { get; set; }

    public string BuildConfigurationJson { get; set; } = "{}";

    public string RemoteServiceName { get; set; } = string.Empty;

    public string RemoteDeploymentDirectory { get; set; } = string.Empty;

    public string ProjectEnvironmentFilePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<KKProjectEnvironmentVariable> EnvironmentVariables { get; set; }
        = new List<KKProjectEnvironmentVariable>();

    public ICollection<KKProjectVersion> Versions { get; set; }
        = new List<KKProjectVersion>();

    public ICollection<KKProjectDeployment> Deployments { get; set; }
        = new List<KKProjectDeployment>();
}
