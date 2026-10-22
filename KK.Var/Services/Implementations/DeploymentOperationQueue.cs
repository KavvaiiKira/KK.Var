using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using Microsoft.Extensions.Logging;

namespace KK.Var.Services.Implementations
{
    public sealed class DeploymentOperationQueue(
        ILocalizationService localizationService,
        ILogger<DeploymentOperationQueue> logger) : IDeploymentOperationQueue
    {
        private const string SubscriberFailureMessage = "Deployment queue change subscriber failed.";

        private readonly object _syncRoot = new object();

        private readonly Queue<IQueueWorkItem> _pendingItems = new Queue<IQueueWorkItem>();

        private readonly List<QueueItemState> _items = new List<QueueItemState>();

        private bool _isProcessing;

        public event EventHandler? QueueChanged;

        public IReadOnlyList<DeploymentQueueItem> GetItems()
        {
            lock (_syncRoot)
            {
                return _items.Select(item => item.Snapshot()).ToArray();
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

        public bool HasActiveOperations()
        {
            lock (_syncRoot)
            {
                return _items.Any(item =>
                    item.Status is DeploymentQueueStatus.Waiting or
                        DeploymentQueueStatus.Running);
            }
        }

        public bool HasActiveOperationForMachine(Guid machineId)
        {
            lock (_syncRoot)
            {
                return _items.Any(item => item.RemoteMachineId == machineId &&
                    item.Status is DeploymentQueueStatus.Waiting or DeploymentQueueStatus.Running);
            }
        }

        public Task<TResult> EnqueueAsync<TResult>(
            Guid projectId,
            string version,
            DeploymentOperationType operationType,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default,
            Guid? remoteMachineId = null)
        {
            ArgumentNullException.ThrowIfNull(operation);

            if (projectId == Guid.Empty)
            {
                throw new ArgumentException(localizationService.Get("Укажите идентификатор проекта."), nameof(projectId));
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException(localizationService.Get("Укажите версию."), nameof(version));
            }

            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            var item = new QueueItemState
            {
                ProjectId = projectId,
                RemoteMachineId = remoteMachineId,
                Version = version.Trim(),
                OperationType = operationType,
                CancellationTokenSource = cancellationTokenSource,
            };
            var workItem = new QueueWorkItem<TResult>(item, operation);
            var startProcessor = false;
            var wasEnqueued = false;

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
                    wasEnqueued = true;
                    workItem.CancellationRegistration = cancellationTokenSource.Token.Register(
                        () => CancelFromToken(item.Id));
                }

                if (!_isProcessing && _pendingItems.Count > 0)
                {
                    _isProcessing = true;
                    startProcessor = true;
                }
            }

            if (!wasEnqueued)
            {
                cancellationTokenSource.Dispose();
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
                    if (_pendingItems.Count == 0)
                    {
                        _isProcessing = false;
                        return;
                    }

                    workItem = _pendingItems.Dequeue();
                    if (workItem.Item.Status == DeploymentQueueStatus.Waiting)
                    {
                        workItem.Item.Status = DeploymentQueueStatus.Running;
                    }
                }

                if (workItem.Item.Status == DeploymentQueueStatus.Cancelled)
                {
                    workItem.CancellationRegistration.Dispose();
                    workItem.Item.CancellationTokenSource.Dispose();
                    continue;
                }

                NotifyQueueChanged();
                var wasCancelled = await workItem.ExecuteAsync();

                lock (_syncRoot)
                {
                    workItem.Item.Status = wasCancelled ?
                        DeploymentQueueStatus.Cancelled :
                        DeploymentQueueStatus.Completed;
                }

                NotifyQueueChanged();
                workItem.CancellationRegistration.Dispose();
                workItem.CompleteExecution();
                workItem.Item.CancellationTokenSource.Dispose();
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
                        SubscriberFailureMessage);
                }
            }
        }

        private sealed class QueueItemState
        {
            public Guid Id { get; } = Guid.NewGuid();
            public Guid ProjectId { get; init; }
            public Guid? RemoteMachineId { get; init; }
            public string Version { get; init; } = string.Empty;
            public DeploymentOperationType OperationType { get; init; }
            public DateTime AddedAtUtc { get; } = DateTime.UtcNow;
            public required CancellationTokenSource CancellationTokenSource { get; init; }
            public DeploymentQueueStatus Status { get; set; } = DeploymentQueueStatus.Waiting;

            public DeploymentQueueItem Snapshot() => new DeploymentQueueItem
            {
                Id = Id,
                ProjectId = ProjectId,
                RemoteMachineId = RemoteMachineId,
                Version = Version,
                OperationType = OperationType,
                AddedAtUtc = AddedAtUtc,
                Status = Status,
            };
        }

        private interface IQueueWorkItem
        {
            QueueItemState Item { get; }

            CancellationTokenRegistration CancellationRegistration { get; set; }

            Task<bool> ExecuteAsync();

            void CompleteExecution();

            void CancelBeforeExecution();
        }

        private sealed class QueueWorkItem<TResult>(
            QueueItemState item,
            Func<CancellationToken, Task<TResult>> operation) : IQueueWorkItem
        {
            private readonly TaskCompletionSource<TResult> _completion =
                new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            private TResult? _result;
            private Exception? _exception;
            private bool _wasCancelled;

            public QueueItemState Item { get; } = item;

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
}
