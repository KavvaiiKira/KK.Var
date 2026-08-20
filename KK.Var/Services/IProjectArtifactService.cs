using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IProjectArtifactService
{
    Task<ProjectArtifact> CreateAsync(
        KKProject project,
        string versionTag,
        string remoteArchitecture,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task DeleteAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
