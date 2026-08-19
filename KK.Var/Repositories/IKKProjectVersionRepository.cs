using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Repositories;

public interface IKKProjectVersionRepository
{
    Task<IReadOnlyList<KKProjectVersion>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<KKProjectVersion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<bool> TagExistsAsync(
        Guid projectId,
        string tag,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        KKProjectVersion version,
        CancellationToken cancellationToken = default);
}
