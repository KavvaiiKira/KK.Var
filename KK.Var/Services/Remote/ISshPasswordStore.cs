using System;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface ISshPasswordStore
    {
        Task SaveAsync(Guid machineId, string password, CancellationToken cancellationToken = default);

        Task<string?> LoadAsync(Guid machineId, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid machineId, CancellationToken cancellationToken = default);
    }
}
