using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Repositories;

public interface IKKProjectEnvironmentVariableRepository
{
    Task<IReadOnlyList<KKProjectEnvironmentVariable>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid projectId,
        EnvironmentFileFormat format,
        IReadOnlyCollection<KKProjectEnvironmentVariable> variables,
        CancellationToken cancellationToken = default);
}
