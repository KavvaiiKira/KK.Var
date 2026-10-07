using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IVersionCleanupService
{
    Task<VersionCleanupPlan> PlanAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<VersionCleanupPlan> ExecuteAsync(
        Guid projectId,
        VersionCleanupPlan? expectedPlan = null,
        bool afterSuccessfulDeploy = false,
        CancellationToken cancellationToken = default);
}
