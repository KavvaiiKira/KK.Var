using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;

namespace KK.Var.Services;

public interface IKKProjectEnvironmentService
{
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid projectId,
        EnvironmentFileFormat format,
        IReadOnlyList<KeyValuePair<string, string>> variables,
        CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<string> WriteFileAsync(
        Guid projectId,
        string projectRootDirectory,
        CancellationToken cancellationToken = default);
}
