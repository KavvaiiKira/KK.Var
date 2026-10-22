using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IArtifactStorageRelocationService
    {
        Task<ArtifactStorageMigrationPlan> PlanMigrationAsync(
            string? targetRoot,
            CancellationToken cancellationToken = default);

        Task<ArtifactStorageMigrationResult> MigrateAsync(
            string? targetRoot,
            CancellationToken cancellationToken = default);
    }
}
