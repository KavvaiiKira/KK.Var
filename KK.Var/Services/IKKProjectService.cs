using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IKKProjectService
{
    Task<IReadOnlyList<KKProject>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<KKProject?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<KKProject> CreateAsync(
        KKProject project,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        KKProject project,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
