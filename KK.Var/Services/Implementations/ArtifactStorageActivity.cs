using System;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services.Implementations
{
    public sealed class ArtifactStorageActivity : IArtifactStorageActivity
    {
        private readonly SemaphoreSlim _migrationLock = new SemaphoreSlim(1, 1);

        private int _migrationRunning;

        private int _cleanupRunning;

        public bool IsMigrationRunning => Volatile.Read(ref _migrationRunning) != 0;

        public bool IsCleanupRunning => Volatile.Read(ref _cleanupRunning) != 0;

        public async Task<IAsyncDisposable> AcquireCleanupLeaseAsync(
            CancellationToken cancellationToken = default)
        {
            await _migrationLock.WaitAsync(cancellationToken);
            Interlocked.Exchange(ref _cleanupRunning, 1);
            return new CleanupLease(this, false);
        }

        private sealed class CleanupLease(ArtifactStorageActivity owner, bool migration) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                if (migration)
                {
                    Interlocked.Exchange(ref owner._migrationRunning, 0);
                }
                else
                {
                    Interlocked.Exchange(ref owner._cleanupRunning, 0);
                }
                owner._migrationLock.Release();
                return ValueTask.CompletedTask;
            }
        }

        public async Task<IAsyncDisposable> AcquireMigrationLeaseAsync(CancellationToken cancellationToken = default)
        {
            await _migrationLock.WaitAsync(cancellationToken);
            Interlocked.Exchange(ref _migrationRunning, 1);
            return new CleanupLease(this, true);
        }
    }
}
