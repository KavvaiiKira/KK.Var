using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface ISourcePreflightService
    {
        Task<(long Bytes, string? CommitSha)> CheckSourceAsync(
            KKProject project,
            DeploymentRequest request,
            List<DeploymentPreflightCheck> checks,
            CancellationToken cancellationToken);
    }
}
