using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IDeploymentOperationQueue
{
    event EventHandler? QueueChanged;

    IReadOnlyList<DeploymentQueueItem> GetItems();

    bool HasActiveOperation(Guid projectId);

    Task<TResult> EnqueueAsync<TResult>(
        Guid projectId,
        string version,
        DeploymentOperationType operationType,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    bool Cancel(Guid itemId);
}
