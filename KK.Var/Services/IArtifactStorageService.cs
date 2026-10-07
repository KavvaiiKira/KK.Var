using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IArtifactStorageService
{
    bool IsMigrationRunning { get; }

    bool IsCleanupRunning { get; }

    Task<IAsyncDisposable> AcquireCleanupLeaseAsync(
        CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default);

    Task DeleteArtifactAsync(
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<ArtifactStorageMigrationPlan> PlanMigrationAsync(
        string? targetRoot,
        CancellationToken cancellationToken = default);

    Task<ArtifactStorageMigrationResult> MigrateAsync(
        string? targetRoot,
        CancellationToken cancellationToken = default);
}
