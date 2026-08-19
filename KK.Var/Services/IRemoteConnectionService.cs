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
}
