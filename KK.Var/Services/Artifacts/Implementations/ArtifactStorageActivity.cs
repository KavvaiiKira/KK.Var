using System;
using System.Threading;
using System.Threading.Tasks;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class ArtifactStorageActivity : IArtifactStorageActivity
    {
        private readonly SemaphoreSlim _migrationLock = new SemaphoreSlim(1, 1);

        private readonly object _syncRoot = new object();

        private int _operations;

        private int _migrationRunning;

        private int _cleanupRunning;

        public bool IsMigrationRunning => Volatile.Read(ref _migrationRunning) != 0;

        public bool IsCleanupRunning => Volatile.Read(ref _cleanupRunning) != 0;

        public Task<IAsyncDisposable> AcquireCleanupLeaseAsync(
            CancellationToken cancellationToken = default) =>
            AcquireExclusiveLeaseAsync(false, null, cancellationToken);

        public IArtifactStorageOperationLease AcquireOperationLease(
            string unavailableMessage,
            CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsMigrationRunning || IsCleanupRunning)
                {
                    throw new InvalidOperationException(unavailableMessage);
                }

                _operations++;
                return new OperationLease(this);
            }
        }

        private async Task<IAsyncDisposable> AcquireExclusiveLeaseAsync(
            bool migration,
            OperationLease? operation,
            CancellationToken cancellationToken)
        {
            await _migrationLock.WaitAsync(cancellationToken);
            try
            {
                lock (_syncRoot)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (operation is not null)
                    {
                        ObjectDisposedException.ThrowIf(operation.IsDisposed, operation);
                    }
                    else if (_operations != 0)
                    {
                        throw new InvalidOperationException(migration ?
                            Texts.ArchiveDirectoryCannotChangedDuringActiveOperation :
                            Texts.VersionsCannotCleanedUpDuringActiveOperation);
                    }

                    if (migration)
                    {
                        Volatile.Write(ref _migrationRunning, 1);
                    }
                    else
                    {
                        Volatile.Write(ref _cleanupRunning, 1);
                    }
                    return new CleanupLease(this, migration);
                }
            }
            catch
            {
                _migrationLock.Release();
                throw;
            }
        }

        private sealed class OperationLease(ArtifactStorageActivity owner) : IArtifactStorageOperationLease
        {
            public bool IsDisposed { get; private set; }

            public Task<IAsyncDisposable> AcquireCleanupLeaseAsync(CancellationToken cancellationToken = default) =>
                owner.AcquireExclusiveLeaseAsync(false, this, cancellationToken);

            public ValueTask DisposeAsync()
            {
                lock (owner._syncRoot)
                {
                    if (!IsDisposed)
                    {
                        IsDisposed = true;
                        owner._operations--;
                    }
                }
                return ValueTask.CompletedTask;
            }
        }

        private sealed class CleanupLease(ArtifactStorageActivity owner, bool migration) : IAsyncDisposable
        {
            private bool _disposed;

            public ValueTask DisposeAsync()
            {
                lock (owner._syncRoot)
                {
                    if (_disposed)
                    {
                        return ValueTask.CompletedTask;
                    }
                    _disposed = true;
                    if (migration)
                    {
                        Volatile.Write(ref owner._migrationRunning, 0);
                    }
                    else
                    {
                        Volatile.Write(ref owner._cleanupRunning, 0);
                    }
                }
                owner._migrationLock.Release();
                return ValueTask.CompletedTask;
            }
        }

        public Task<IAsyncDisposable> AcquireMigrationLeaseAsync(CancellationToken cancellationToken = default) =>
            AcquireExclusiveLeaseAsync(true, null, cancellationToken);
    }
}
