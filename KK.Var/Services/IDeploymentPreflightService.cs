using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IDeploymentPreflightService
{
    Task<DeploymentPreflightResult> CheckAsync(
        DeploymentRequest request,
        bool currentOperationOwnsQueue = false,
        CancellationToken cancellationToken = default);
}
