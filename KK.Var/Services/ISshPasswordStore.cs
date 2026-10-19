using System.Threading;
using System.Threading.Tasks;
using System;

namespace KK.Var.Services;

public interface ISshPasswordStore
{
    Task SaveAsync(Guid machineId, string password, CancellationToken cancellationToken = default);
    Task<string?> LoadAsync(Guid machineId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid machineId, CancellationToken cancellationToken = default);
    Task SaveAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        CancellationToken cancellationToken = default);
}
