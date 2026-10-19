using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IRemoteMachineService
{
    Task<IReadOnlyList<RemoteMachine>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RemoteMachine> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RemoteMachineSettings> GetConnectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RemoteMachine> SaveVerifiedAsync(RemoteMachine candidate, RemoteMachineSettings connection,
        RemoteConnectionCheckResult verification, DateTime? expectedUpdatedAt, bool identityConfirmed,
        CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IAsyncDisposable> ReserveAsync(Guid id, CancellationToken cancellationToken = default);
    Task AssertEditableAsync(Guid id, CancellationToken cancellationToken = default);
}
