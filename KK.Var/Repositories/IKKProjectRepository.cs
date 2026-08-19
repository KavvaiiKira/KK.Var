using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Repositories;

public interface IKKProjectRepository
{
    Task<IReadOnlyList<KKProject>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<KKProject?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string name,
        Guid? excludedProjectId = null,
        CancellationToken cancellationToken = default);

    Task<bool> ServiceNameExistsAsync(
        string serviceName,
        Guid? excludedProjectId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        KKProject project,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        KKProject project,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
