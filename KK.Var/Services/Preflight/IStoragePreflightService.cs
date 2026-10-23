using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IStoragePreflightService
    {
        Task CheckLocalStorageAsync(
            List<DeploymentPreflightCheck> checks,
            long minimumFreeBytes,
            CancellationToken cancellationToken);

        void CheckRemotePath(
            KKProject project,
            List<DeploymentPreflightCheck> checks);
    }
}
