using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IRemoteConnectionService
{
    Task<RemoteConnectionCheckResult> CheckAsync(
        RemoteMachineSettings settings,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentPreflightCheck>> CheckDeploymentAsync(
        RemoteMachineSettings settings,
        string deploymentDirectory,
        long minimumFreeBytes,
        bool requiresPython,
        CancellationToken cancellationToken = default);
}
