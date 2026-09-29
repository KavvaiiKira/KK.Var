using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IKKProjectVersionService
{
    Task<IReadOnlyList<KKProjectVersion>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<KKProjectVersion> CreateAsync(
        KKProjectVersion version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetProtectedVersionIdsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task SetPinnedAsync(
        Guid versionId,
        bool isPinned,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);
}
