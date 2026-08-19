using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Repositories;

public interface IKKProjectEnvironmentVariableRepository
{
    Task<IReadOnlyList<KKProjectEnvironmentVariable>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid projectId,
        IReadOnlyCollection<KKProjectEnvironmentVariable> variables,
        CancellationToken cancellationToken = default);
}
