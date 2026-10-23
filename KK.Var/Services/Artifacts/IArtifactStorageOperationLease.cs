using System;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface IArtifactStorageOperationLease : IAsyncDisposable
    {
        Task<IAsyncDisposable> AcquireCleanupLeaseAsync(CancellationToken cancellationToken = default);
    }
}
