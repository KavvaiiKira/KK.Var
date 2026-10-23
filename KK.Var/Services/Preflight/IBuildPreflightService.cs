using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IBuildPreflightService
    {
        Task CheckBuildConfigurationAsync(
            KKProject project,
            List<DeploymentPreflightCheck> checks,
            CancellationToken cancellationToken);
    }
}
