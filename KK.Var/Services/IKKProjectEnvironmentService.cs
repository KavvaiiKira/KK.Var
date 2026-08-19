using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services;

public interface IKKProjectEnvironmentService
{
    Task<IReadOnlyDictionary<string, string>> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid projectId,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default);

    Task<string> GenerateJsonAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<string> WriteJsonFileAsync(
        Guid projectId,
        string projectRootDirectory,
        CancellationToken cancellationToken = default);
}
