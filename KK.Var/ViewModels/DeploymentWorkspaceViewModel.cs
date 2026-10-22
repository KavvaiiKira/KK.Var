using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public partial class DeploymentWorkspaceViewModel : LocalizedViewModel
    {
        private readonly Dictionary<Guid, DeploymentUiState> _deploymentStates =
            new Dictionary<Guid, DeploymentUiState>();

        [ObservableProperty]
        public partial bool IsDeploymentRunning { get; set; }

        public bool IsSelectedProjectDeploymentRunning =>
            SelectedDeploymentState?.QueueStatus == DeploymentQueueStatus.Running;

        public bool IsSelectedProjectDeploymentWaiting =>
            SelectedDeploymentState?.QueueStatus == DeploymentQueueStatus.Waiting;

        public bool IsSelectedProjectDeploymentActive =>
            SelectedDeploymentState?.IsActive == true;

        public int DeploymentProgressPercentage =>
            SelectedDeploymentState?.ProgressPercentage ?? 0;

        public string DeploymentProgressMessage =>
            SelectedDeploymentState?.ProgressMessage ?? string.Empty;

        public string VisibleDeploymentLogText =>
            SelectedDeploymentState?.LogText ?? string.Empty;

        public bool HasDeploymentPreflightResult =>
            SelectedDeploymentState?.PreflightResult is not null;

        public string DeploymentPreflightSummary
        {
            get
            {
                var result = SelectedDeploymentState?.PreflightResult;
                return result is null ? string.Empty : FormatPreflightSummary(result);
            }
        }

        public string DeploymentPreflightDetails =>
            SelectedDeploymentState?.PreflightResult is { } result ?
                string.Join(
                    Environment.NewLine,
                    result.Checks.Select(check =>
                                    $"{Localize(check.Status switch
                                    {
                                        PreflightStatus.Success => "Успешно",
                                        PreflightStatus.Warning => "Предупреждение",
                                        _ => "Ошибка",
                                    })}: {check.Message}" +
                                    (string.IsNullOrWhiteSpace(check.Detail) ?
                                        string.Empty :
                                        $" — {check.Detail}"))) :
                string.Empty;

        public bool HasVisibleDeploymentLog => !string.IsNullOrWhiteSpace(VisibleDeploymentLogText);

        public string SelectedProjectQueueStatusText
        {
            get
            {
                var state = SelectedDeploymentState;
                if (state?.QueueStatus == DeploymentQueueStatus.Waiting)
                {
                    return LocalizeFormat(
                        "В очереди · позиция {0}",
                        GetWaitingPosition(state));
                }

                if (state?.QueueStatus == DeploymentQueueStatus.Running)
                {
                    return string.IsNullOrWhiteSpace(state.ProgressMessage) ?
                        Localize("Выполняется") :
                        state.ProgressMessage;
                }

                return string.Empty;
            }
        }

        internal DeploymentUiState? SelectedDeploymentState =>
            SelectedProjectId.HasValue &&
            _deploymentStates.TryGetValue(SelectedProjectId.Value, out var state) ?
                state :
                null;

        internal void HandleDeploymentProgress(
            Guid projectId,
            Guid operationId,
            DeploymentProgress progress)
        {
            if (progress.Percentage >= 0 &&
                TryGetDeploymentState(projectId, operationId, out var state))
            {
                state.ProgressPercentage = progress.Percentage;
                state.ProgressMessage = progress.Message;

                AppendDeploymentLog(state, progress.Message);
            }
        }

        internal void AppendDeploymentLog(DeploymentUiState state, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";

            var lines = string.IsNullOrEmpty(state.LogText) ?
                new List<string>() :
                state.LogText.Split(Environment.NewLine).ToList();

            lines.Add(line);

            if (lines.Count > 250)
            {
                lines.RemoveRange(0, lines.Count - 250);
            }

            state.LogText = string.Join(Environment.NewLine, lines);
            NotifySelectedDeploymentStateChanged();
        }

        internal void NotifySelectedDeploymentStateChanged()
        {
            OnPropertyChanged(nameof(IsSelectedProjectDeploymentActive));
            OnPropertyChanged(nameof(IsSelectedProjectDeploymentWaiting));
            OnPropertyChanged(nameof(IsSelectedProjectDeploymentRunning));
            OnPropertyChanged(nameof(DeploymentProgressPercentage));
            OnPropertyChanged(nameof(DeploymentProgressMessage));
            OnPropertyChanged(nameof(SelectedProjectQueueStatusText));
            OnPropertyChanged(nameof(VisibleDeploymentLogText));
            OnPropertyChanged(nameof(HasVisibleDeploymentLog));
            OnPropertyChanged(nameof(HasDeploymentPreflightResult));
            OnPropertyChanged(nameof(DeploymentPreflightSummary));
            OnPropertyChanged(nameof(DeploymentPreflightDetails));
            OnPropertyChanged(nameof(DeploymentPreflightItems));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void HandleDeploymentPreflight(
            Guid projectId,
            Guid operationId,
            DeploymentPreflightResult result)
        {
            if (!TryGetDeploymentState(projectId, operationId, out var state))
            {
                return;
            }

            if (state.PreflightRevision != GetPreflightRevision(projectId))
            {
                return;
            }

            state.PreflightResult = result;
            AppendDeploymentLog(state, FormatPreflightSummary(result));
            NotifySelectedDeploymentStateChanged();
        }

        private string FormatPreflightSummary(DeploymentPreflightResult result)
        {
            if (result.FirstError is { } error)
            {
                return LocalizeFormat("Preflight: ошибка — {0}", error.Message);
            }

            var warnings = result.Checks.Count(check => check.Status == PreflightStatus.Warning);
            return warnings == 0 ?
                Localize("Preflight: проверка пройдена.") :
                LocalizeFormat("Preflight: предупреждений — {0}.", warnings);
        }

        private void DeploymentOperationQueue_OnQueueChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                RefreshDeploymentQueueState();
                return;
            }

            Dispatcher.UIThread.Post(RefreshDeploymentQueueState);
        }

        private void RefreshDeploymentQueueState()
        {
            var items = _deploymentOperationQueue.GetItems();

            foreach (var state in _deploymentStates.Values)
            {
                var item = state.QueueItemId.HasValue ?
                    items.SingleOrDefault(candidate => candidate.Id == state.QueueItemId.Value) :
                    items.LastOrDefault(candidate =>
                        candidate.ProjectId == state.ProjectId &&
                        candidate.Version == state.VersionTag &&
                        candidate.Status is DeploymentQueueStatus.Waiting or
                            DeploymentQueueStatus.Running);

                if (item is null)
                {
                    continue;
                }

                state.QueueItemId = item.Id;
                state.QueueStatus = item.Status;
            }

            IsDeploymentRunning = items.Any(item =>
                item.Status is DeploymentQueueStatus.Waiting or DeploymentQueueStatus.Running);
            NotifySelectedDeploymentStateChanged();
        }

        private int GetWaitingPosition(DeploymentUiState state)
        {
            var waitingItems = _deploymentOperationQueue
                .GetItems()
                .Where(item => item.Status == DeploymentQueueStatus.Waiting)
                .OrderBy(item => item.AddedAtUtc)
                .ToList();
            var index = waitingItems.FindIndex(item => item.Id == state.QueueItemId);

            return index < 0 ? 1 : index + 1;
        }

        internal bool TryGetDeploymentState(
            Guid projectId,
            Guid operationId,
            out DeploymentUiState state)
        {
            if (_deploymentStates.TryGetValue(projectId, out var candidate) &&
                candidate.OperationId == operationId)
            {
                state = candidate;
                return true;
            }

            state = null!;
            return false;
        }

        internal void CompleteUiStateIfDetached(DeploymentUiState state)
        {
            if (TryGetDeploymentState(state.ProjectId, state.OperationId, out var currentState) &&
                currentState.IsActive &&
                (!_deploymentOperationQueue.HasActiveOperation(state.ProjectId)))
            {
                currentState.QueueStatus = DeploymentQueueStatus.Completed;
            }

            RefreshDeploymentQueueState();
        }

        private readonly Dictionary<Guid, int> _preflightRevisions = new Dictionary<Guid, int>();

        internal int GetPreflightRevision(Guid projectId) =>
            _preflightRevisions.GetValueOrDefault(projectId);

        public DeploymentWorkspaceViewModel(
            IDeploymentOperationQueue operationQueue,
            ILocalizationService localizationService)
            : base(localizationService)
        {
            _deploymentOperationQueue = operationQueue;
            operationQueue.QueueChanged += DeploymentOperationQueue_OnQueueChanged;
        }

        private readonly IDeploymentOperationQueue _deploymentOperationQueue;

        internal Guid? SelectedProjectId { get; set; }

        internal event EventHandler? StateChanged;

        public IReadOnlyList<DeploymentPreflightItemViewModel> DeploymentPreflightItems =>
            SelectedDeploymentState?.PreflightItems ?? [];

        internal void SetState(DeploymentUiState state) => _deploymentStates[state.ProjectId] = state;

        internal void RemoveState(Guid projectId) => _deploymentStates.Remove(projectId);

        internal bool TryGetState(Guid projectId, out DeploymentUiState state) =>
            _deploymentStates.TryGetValue(projectId, out state!);

        internal void InvalidatePreflight(Guid? projectId)
        {
            var projectIds = projectId.HasValue ?
                [projectId.Value] :
                _deploymentStates.Keys.ToArray();
            foreach (var id in projectIds)
            {
                _preflightRevisions[id] = GetPreflightRevision(id) + 1;
                if (_deploymentStates.TryGetValue(id, out var state))
                {
                    state.PreflightResult = null;
                }
            }
        }
    }
}
