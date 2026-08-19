using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Repositories;

public interface IKKProjectDeploymentRepository
{
    Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<KKProjectDeployment?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<KKProjectDeployment?> GetLatestSuccessfulAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        KKProjectDeployment deployment,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        KKProjectDeployment deployment,
        CancellationToken cancellationToken = default);
}
