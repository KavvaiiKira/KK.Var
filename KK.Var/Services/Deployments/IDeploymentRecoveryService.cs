using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IDeploymentRecoveryService
    {
        Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);

        Task<KKProjectDeployment> GetAsync(Guid deploymentId, CancellationToken cancellationToken = default);

        Task<string?> ReadLogAsync(Guid deploymentId, CancellationToken cancellationToken = default);

        Task<KKProjectDeployment> RecoverFailedAsync(Guid deploymentId, CancellationToken cancellationToken = default);
    }
}
