using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using KK.Var.Enums;

namespace KK.Var.Models
{
    public sealed class KKProject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public Guid RemoteMachineId { get; set; }

        public RemoteMachine RemoteMachine { get; set; } = null!;

        public string? Description { get; set; }

        [NotMapped]
        public string EffectiveDescription =>
            string.IsNullOrWhiteSpace(Description) ?
                Name :
                Description;

        [NotMapped]
        public string RemoteExecutablePath =>
            $"{RemoteDeploymentDirectory.TrimEnd('/')}/{RemoteExecutableFileName.TrimStart('/')}";

        public ProjectSourceType SourceType { get; set; }

        public string? LocalDirectoryPath { get; set; }

        public long? GitHubRepositoryId { get; set; }

        public string? GitHubRepositoryFullName { get; set; }

        public string? GitHubCloneUrl { get; set; }

        public string? GitHubBranch { get; set; }

        public ProjectBuildProvider BuildProvider { get; set; }

        public string BuildConfigurationJson { get; set; } = "{}";

        public string RemoteServiceName { get; set; } = string.Empty;

        public string RemoteExecutableFileName { get; set; } = string.Empty;

        public string RemoteDeploymentDirectory { get; set; } = string.Empty;

        public string ProjectEnvironmentFilePath { get; set; } = string.Empty;

        public EnvironmentFileFormat EnvironmentFileFormat { get; set; } = EnvironmentFileFormat.Json;

        public ProjectHealthCheckType HealthCheckType { get; set; } = ProjectHealthCheckType.None;

        public int? HealthCheckTimeoutSeconds { get; set; }

        public int? HealthCheckIntervalSeconds { get; set; }

        public int? HealthCheckAttempts { get; set; }

        public int? HealthCheckStabilityDelaySeconds { get; set; }

        public string? HealthCheckHttpUrl { get; set; }

        public int? HealthCheckTcpPort { get; set; }

        public string? HealthCheckCommand { get; set; }

        public int MaxStoredVersions { get; set; } = 10;

        public long? MaxStoredVersionBytes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public ICollection<KKProjectEnvironmentVariable> EnvironmentVariables { get; set; } = new List<KKProjectEnvironmentVariable>();

        public ICollection<KKProjectVersion> Versions { get; set; } = new List<KKProjectVersion>();

        public ICollection<KKProjectDeployment> Deployments { get; set; } = new List<KKProjectDeployment>();
    }
}
