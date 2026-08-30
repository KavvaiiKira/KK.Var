using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using Microsoft.Extensions.Logging;

namespace KK.Var.Services.Implementations;

public sealed class DeploymentOperationQueue(
    ILocalizationService localizationService,
    ILogger<DeploymentOperationQueue> logger) : IDeploymentOperationQueue
{
    private readonly object _syncRoot = new object();
    private readonly Queue<IQueueWorkItem> _pendingItems = new Queue<IQueueWorkItem>();
    private readonly List<DeploymentQueueItem> _items = new List<DeploymentQueueItem>();
    private bool _isProcessing;

    public event EventHandler? QueueChanged;

    public IReadOnlyList<DeploymentQueueItem> GetItems()
    {
        lock (_syncRoot)
        {
            return _items.ToArray();
        }
    }

    public bool HasActiveOperation(Guid projectId)
    {
        lock (_syncRoot)
        {
            return _items.Any(item =>
                item.ProjectId == projectId &&
                item.Status is DeploymentQueueStatus.Waiting or
                    DeploymentQueueStatus.Running);
        }
    }

    public Task<TResult> EnqueueAsync<TResult>(
        Guid projectId,
        string version,
        DeploymentOperationType operationType,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project identifier is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version is required.", nameof(version));
        }

        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var item = new DeploymentQueueItem
        {
            ProjectId = projectId,
            Version = version.Trim(),
            OperationType = operationType,
            CancellationTokenSource = cancellationTokenSource,
        };
        var workItem = new QueueWorkItem<TResult>(item, operation);
        var startProcessor = false;

        lock (_syncRoot)
        {
            if (_items.Any(candidate =>
                candidate.ProjectId == projectId &&
                candidate.Status is DeploymentQueueStatus.Waiting or
                    DeploymentQueueStatus.Running))
            {
                cancellationTokenSource.Dispose();
                throw new InvalidOperationException(localizationService.Get(
                    "Для этого проекта уже есть активная операция."));
            }

            _items.Add(item);

            if (cancellationTokenSource.IsCancellationRequested)
            {
                item.Status = DeploymentQueueStatus.Cancelled;
                workItem.CancelBeforeExecution();
            }
            else
            {
                _pendingItems.Enqueue(workItem);
                workItem.CancellationRegistration = cancellationTokenSource.Token.Register(
                    () => CancelFromToken(item.Id));
            }

            if (!_isProcessing && _pendingItems.Count > 0)
            {
                _isProcessing = true;
                startProcessor = true;
            }
        }

        NotifyQueueChanged();

        if (startProcessor)
        {
            _ = ProcessQueueAsync();
        }

        return workItem.Completion;
    }

    public bool Cancel(Guid itemId)
    {
        IQueueWorkItem? workItem;

        lock (_syncRoot)
        {
            workItem = _pendingItems.SingleOrDefault(candidate => candidate.Item.Id == itemId);
            if (workItem is null || workItem.Item.Status != DeploymentQueueStatus.Waiting)
            {
                return false;
            }

            workItem.Item.Status = DeploymentQueueStatus.Cancelled;
            workItem.CancelBeforeExecution();
        }

        workItem.Item.CancellationTokenSource.Cancel();
        NotifyQueueChanged();
        return true;
    }

    private void CancelFromToken(Guid itemId)
    {
        IQueueWorkItem? workItem = null;

        lock (_syncRoot)
        {
            workItem = _pendingItems.SingleOrDefault(candidate => candidate.Item.Id == itemId);
            if (workItem is null || workItem.Item.Status != DeploymentQueueStatus.Waiting)
            {
                return;
            }

            workItem.Item.Status = DeploymentQueueStatus.Cancelled;
            workItem.CancelBeforeExecution();
        }

        NotifyQueueChanged();
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            IQueueWorkItem? workItem;

            lock (_syncRoot)
            {
                do
                {
                    if (_pendingItems.Count == 0)
                    {
                        _isProcessing = false;
                        return;
                    }

                    workItem = _pendingItems.Dequeue();
                }
                while (workItem.Item.Status == DeploymentQueueStatus.Cancelled);

                workItem.Item.Status = DeploymentQueueStatus.Running;
            }

            NotifyQueueChanged();
            var wasCancelled = await workItem.ExecuteAsync();

            lock (_syncRoot)
            {
                workItem.Item.Status = wasCancelled ?
                    DeploymentQueueStatus.Cancelled :
                    DeploymentQueueStatus.Completed;
                workItem.CancellationRegistration.Dispose();
            }

            NotifyQueueChanged();
            workItem.CompleteExecution();
        }
    }

    private void NotifyQueueChanged()
    {
        var handlers = QueueChanged?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Deployment queue change subscriber failed.");
            }
        }
    }

    private interface IQueueWorkItem
    {
        DeploymentQueueItem Item { get; }

        CancellationTokenRegistration CancellationRegistration { get; set; }

        Task<bool> ExecuteAsync();

        void CompleteExecution();

        void CancelBeforeExecution();
    }

    private sealed class QueueWorkItem<TResult>(
        DeploymentQueueItem item,
        Func<CancellationToken, Task<TResult>> operation) : IQueueWorkItem
    {
        private readonly TaskCompletionSource<TResult> _completion =
            new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TResult? _result;
        private Exception? _exception;
        private bool _wasCancelled;

        public DeploymentQueueItem Item { get; } = item;

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public Task<TResult> Completion => _completion.Task;

        public async Task<bool> ExecuteAsync()
        {
            try
            {
                _result = await operation(Item.CancellationTokenSource.Token);
                return false;
            }
            catch (OperationCanceledException)
            {
                _wasCancelled = true;
                return true;
            }
            catch (Exception exception)
            {
                _exception = exception;
                return false;
            }
        }

        public void CompleteExecution()
        {
            if (_wasCancelled)
            {
                _completion.TrySetCanceled(Item.CancellationTokenSource.Token);
            }
            else if (_exception is not null)
            {
                _completion.TrySetException(_exception);
            }
            else
            {
                _completion.TrySetResult(_result!);
            }
        }

        public void CancelBeforeExecution() =>
            _completion.TrySetCanceled(Item.CancellationTokenSource.Token);
    }
}
