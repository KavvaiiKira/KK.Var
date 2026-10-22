using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IDeploymentExecutor
    {
        Task<KKProjectDeployment> ExecuteAsync(
            KKProject project,
            KKProjectVersion version,
            string artifactPath,
            DeploymentOperationType operationType,
            RemoteMachineSettings connection,
            IProgress<DeploymentProgress>? progress,
            CancellationToken cancellationToken);
    }
}
