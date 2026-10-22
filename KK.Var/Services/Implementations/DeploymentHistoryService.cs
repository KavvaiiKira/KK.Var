using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations
{
    public sealed class DeploymentHistoryService(IKKProjectDeploymentRepository deploymentRepository) : IDeploymentHistoryService
    {
        public Task<KKProjectDeployment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            deploymentRepository.GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
            string? projectName,
            string? searchText,
            DateTime? startedFromUtc,
            DateTime? startedBeforeUtc,
            DeploymentStatus? status,
            int skip,
            int take,
            CancellationToken cancellationToken = default,
            Guid? remoteMachineId = null) =>
            deploymentRepository.SearchAsync(
                projectName,
                searchText,
                startedFromUtc,
                startedBeforeUtc,
                status,
                skip,
                take,
                cancellationToken,
                remoteMachineId);

        public Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            deploymentRepository.GetByProjectIdAsync(projectId, cancellationToken);
    }
}
