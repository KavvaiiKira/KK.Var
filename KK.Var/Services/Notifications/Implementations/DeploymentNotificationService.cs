using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;
using Microsoft.Extensions.Logging;

namespace KK.Var.Services.Implementations
{
    public sealed class DeploymentNotificationService(
        IKKProjectRepository projectRepository,
        IKKProjectDeploymentRepository deploymentRepository,
        ILogger<DeploymentNotificationService> logger) : IDeploymentNotificationService
    {
        private const string PersistedCompletionFailureMessage = "Could not publish persisted deployment completion.";

        private const string SubscriberFailureMessage = "Deployment completion subscriber failed.";

        private const string FailureNotificationFailureMessage = "Could not publish deployment failure notification.";

        public event Action<DeploymentCompletionNotice>? DeploymentCompleted;

        public async Task ReportPersistedCompletionAsync(
            KKProjectDeployment deployment,
            bool automaticRollbackCompleted = false,
            bool automaticRollbackFailed = false)
        {
            try
            {
                var project = await projectRepository.GetByIdAsync(deployment.KKProjectId, CancellationToken.None);
                ReportCompletion(new DeploymentCompletionNotice(
                    deployment.KKProjectId,
                    deployment.Id,
                    project?.Name ?? string.Empty,
                    deployment.Status,
                    deployment.OperationType,
                    automaticRollbackCompleted,
                    automaticRollbackFailed));
            }
            catch (Exception notificationException)
            {
                logger.LogWarning(notificationException, PersistedCompletionFailureMessage);
            }
        }

        private void ReportCompletion(DeploymentCompletionNotice notice)
        {
            foreach (var handler in DeploymentCompleted?.GetInvocationList() ?? [])
            {
                try
                {
                    ((Action<DeploymentCompletionNotice>)handler)(notice);
                }
                catch (Exception notificationException)
                {
                    logger.LogWarning(notificationException, SubscriberFailureMessage);
                }
            }
        }

        public async Task<KKProjectDeployment> WithFailureNotificationAsync(
            Guid projectId,
            string tag,
            DeploymentOperationType operationType,
            Func<Task<KKProjectDeployment>> operation)
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                return await operation();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                try
                {
                    var deployments = await deploymentRepository.GetByProjectIdAsync(
                        projectId,
                        CancellationToken.None);
                    var deployment = deployments.FirstOrDefault(item =>
                        item.VersionTag == tag.Trim() && item.StartedAtUtc >= startedAt);
                    if (deployment is null || deployment.Status is DeploymentStatus.Pending or DeploymentStatus.Running)
                    {
                        var project = await projectRepository.GetByIdAsync(projectId, CancellationToken.None);
                        ReportCompletion(new DeploymentCompletionNotice(
                            projectId,
                            deployment?.Id,
                            project?.Name ?? string.Empty,
                            DeploymentStatus.Failed,
                            operationType));
                    }
                }
                catch (Exception notificationException)
                {
                    logger.LogWarning(notificationException, FailureNotificationFailureMessage);
                }
                throw;
            }
        }
    }
}
