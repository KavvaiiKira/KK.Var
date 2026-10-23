using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IDeploymentHistoryService
    {
        Task<KKProjectDeployment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
            string? projectName,
            string? searchText,
            DateTime? startedFromUtc,
            DateTime? startedBeforeUtc,
            DeploymentStatus? status,
            int skip,
            int take,
            CancellationToken cancellationToken = default,
            Guid? remoteMachineId = null);

        Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
            Guid projectId,
            CancellationToken cancellationToken = default);
    }
}
