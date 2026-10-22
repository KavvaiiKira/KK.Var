using System;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface IArtifactStorageActivity
    {
        bool IsMigrationRunning { get; }

        bool IsCleanupRunning { get; }

        Task<IAsyncDisposable> AcquireCleanupLeaseAsync(CancellationToken cancellationToken = default);

        Task<IAsyncDisposable> AcquireMigrationLeaseAsync(CancellationToken cancellationToken = default);
    }
}
