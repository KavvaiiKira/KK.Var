using System;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IDeploymentNotificationService
    {
        event Action<DeploymentCompletionNotice>? DeploymentCompleted;

        Task ReportPersistedCompletionAsync(
            KKProjectDeployment deployment,
            bool automaticRollbackCompleted = false,
            bool automaticRollbackFailed = false);

        Task<KKProjectDeployment> WithFailureNotificationAsync(
            Guid projectId,
            string tag,
            DeploymentOperationType operationType,
            Func<Task<KKProjectDeployment>> operation);
    }
}
