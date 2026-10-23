using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IArtifactStorageService
    {
        Task<string> GetEffectiveRootAsync(
            CancellationToken cancellationToken = default);

        Task<ArtifactStorageSummary> GetSummaryAsync(
            CancellationToken cancellationToken = default);

        Task<ArtifactFileState> GetFileStateAsync(
            string relativePath,
            CancellationToken cancellationToken = default);

        Task<string> ResolvePathAsync(
            string relativePath,
            CancellationToken cancellationToken = default);

        Task DeleteProjectArtifactsAsync(
            Guid projectId,
            CancellationToken cancellationToken = default,
            IArtifactStorageDestructiveLease? reservation = null);

        Task DeleteArtifactAsync(
            string relativePath,
            CancellationToken cancellationToken = default);
    }
}
