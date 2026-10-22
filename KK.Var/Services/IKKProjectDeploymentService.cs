using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IKKProjectDeploymentService
    {
        Task<KKProjectDeployment> DeployAsync(
            DeploymentRequest request,
            IProgress<DeploymentProgress>? progress = null,
            IProgress<DeploymentPreflightResult>? preflightProgress = null,
            CancellationToken cancellationToken = default);

        Task<KKProjectDeployment> RollbackAsync(
            Guid projectId,
            Guid versionId,
            IProgress<DeploymentProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }
}
