using System;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface IArtifactStorageActivity
    {
        bool IsMigrationRunning { get; }

        bool IsCleanupRunning { get; }

        IArtifactStorageDestructiveLease AcquireDestructiveLease(CancellationToken cancellationToken = default);

        void ValidateDestructiveLease(IArtifactStorageDestructiveLease lease);

        IArtifactStorageOperationLease AcquireOperationLease(string unavailableMessage, CancellationToken cancellationToken = default);

        Task<IAsyncDisposable> AcquireCleanupLeaseAsync(CancellationToken cancellationToken = default);

        Task<IAsyncDisposable> AcquireMigrationLeaseAsync(CancellationToken cancellationToken = default);
    }
}
