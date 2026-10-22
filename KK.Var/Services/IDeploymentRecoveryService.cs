using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface IDeploymentRecoveryService
    {
        Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
    }
}
