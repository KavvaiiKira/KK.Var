using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IRemoteDeploymentService
{
    Task DeployAsync(
        KKProject project,
        string artifactPath,
        RemoteMachineSettings settings,
        TextWriter log,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
