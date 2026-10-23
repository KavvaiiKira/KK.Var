using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public sealed partial class DeploymentDetailsViewModel(
        KKProjectDeployment deployment,
        string? logText,
        ILocalizationService localizationService,
        IDeploymentRecoveryService? recoveryService = null) : ViewModelBase
    {
        private const string DurationTimeFormat = "hh\\:mm\\:ss";

        public KKProjectDeployment Deployment { get; private set; } = deployment;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanRecover))]
        [NotifyPropertyChangedFor(nameof(RecoveryNotice))]
        [NotifyCanExecuteChangedFor(nameof(RecoverCommand))]
        public partial bool IsRecoveryRunning { get; set; }

        [ObservableProperty]
        public partial string RecoveryMessage { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasRecoveryLog))]
        public partial string RecoveryLogDisplay { get; set; } = string.Empty;

        public bool HasRecoveryLog => !string.IsNullOrWhiteSpace(RecoveryLogDisplay);

        public bool HasRecoveryHistory => DeploymentRecoveryAttempt.Read(Deployment.RecoveryAttemptsJson).Count > 0;

        public bool CanRecover => recoveryService is not null && Deployment.Status == DeploymentStatus.Failed &&
            Deployment.RecoveryRequired && !Deployment.RecoverySuperseded && !Deployment.RecoveryInProgress && !IsRecoveryRunning;

        public bool HasRecovery => Deployment.RecoveryRequired || DeploymentRecoveryAttempt.Read(Deployment.RecoveryAttemptsJson).Count > 0;

        public string RecoveryNotice => IsRecoveryRunning || Deployment.RecoveryInProgress ?
            Texts.RecoveryInProgress : Deployment.RecoverySuperseded ?
            Texts.RecoverySuperseded :
            Deployment.RecoveryRequired ? Texts.RecoveryRequiredNotice : Texts.RecoveryResolvedNotice;

        public string RecoveryHistoryDisplay => string.Join(Environment.NewLine + Environment.NewLine,
            DeploymentRecoveryAttempt.Read(Deployment.RecoveryAttemptsJson).Select(attempt =>
                $"{attempt.StartedAtUtc.ToLocalTime():G} - {AttemptStatus(attempt.Status)}" +
                (attempt.Outcome is { } outcome ? Environment.NewLine + OutcomeText(outcome) : string.Empty) +
                (attempt.ErrorMessage is { } error ? Environment.NewLine + Localize(error) : string.Empty) +
                Environment.NewLine + attempt.LogPath));

        [RelayCommand]
        public async Task RefreshRecoveryAsync()
        {
            if (recoveryService is null)
            {
                return;
            }
            Deployment = await recoveryService.GetAsync(Deployment.Id);
            RecoveryLogDisplay = await recoveryService.ReadLogAsync(Deployment.Id) ?? string.Empty;
            OnPropertyChanged(string.Empty);
            RecoverCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanRecover), IncludeCancelCommand = true)]
        private async Task RecoverAsync(CancellationToken cancellationToken)
        {
            if (!CanRecover)
            {
                return;
            }
            IsRecoveryRunning = true;
            RecoveryMessage = string.Empty;
            try
            {
                Deployment = await recoveryService!.RecoverFailedAsync(Deployment.Id, cancellationToken);
                RecoveryMessage = OutcomeText(DeploymentRecoveryAttempt.Read(Deployment.RecoveryAttemptsJson).Last().Outcome!.Value);
            }
            catch (OperationCanceledException)
            {
                RecoveryMessage = Texts.RecoveryCancelledNotice;
            }
            catch (Exception exception)
            {
                RecoveryMessage = Localize(exception.Message);
            }
            finally
            {
                try
                {
                    await RefreshRecoveryAsync();
                }
                catch (Exception exception)
                {
                    RecoveryMessage = Localize(exception.Message);
                }
                IsRecoveryRunning = false;
            }
        }

        private static string AttemptStatus(DeploymentRecoveryAttemptStatus status) => status switch
        {
            DeploymentRecoveryAttemptStatus.Succeeded => Texts.RecoveryAttemptSucceeded,
            DeploymentRecoveryAttemptStatus.Failed => Texts.RecoveryAttemptFailed,
            DeploymentRecoveryAttemptStatus.Cancelled => Texts.RecoveryAttemptCancelled,
            _ => Texts.RecoveryAttemptUnknown,
        };

        private static string OutcomeText(DeploymentRecoveryOutcome outcome) => outcome switch
        {
            DeploymentRecoveryOutcome.PreviousVersionRestored => Texts.InterruptedDeployRecoveredPreviousVersionRestored,
            DeploymentRecoveryOutcome.NewVersionRemoved => Texts.RecoveryNewVersionRemoved,
            DeploymentRecoveryOutcome.PreviousFilesRestored => Texts.RecoveryPreviousFilesRestored,
            DeploymentRecoveryOutcome.StagingCleaned => Texts.InterruptedDeployStoppedVersionSwitch,
            _ => Texts.RecoveryOutcomeUnknown,
        };

        public string ProjectName => Deployment.Project?.Name ?? string.Empty;

        public string MachineDisplay => $"{Deployment.RemoteMachineName} · {Deployment.RemoteUserName}@{Deployment.RemoteHost}:{Deployment.RemotePort} · {Deployment.RemoteArchitecture}";

        public string VersionTag => Deployment.Version?.Tag ?? Deployment.VersionTag;

        public string OperationDisplay => Deployment.OperationType switch
        {
            DeploymentOperationType.Deploy => Texts.Deployment,
            DeploymentOperationType.Rollback => Texts.Rollback,
            _ => Deployment.OperationType.ToString(),
        };

        public string StatusDisplay => Deployment.Status switch
        {
            DeploymentStatus.Pending => Texts.Pending,
            DeploymentStatus.Running => Texts.Running,
            DeploymentStatus.Succeeded => Texts.Succeeded,
            DeploymentStatus.Failed => Texts.Failed,
            DeploymentStatus.Cancelled => Texts.Cancelled,
            DeploymentStatus.Interrupted => Texts.Interrupted,
            _ => Deployment.Status.ToString(),
        };

        public string StartedAtDisplay => Deployment.StartedAtUtc.ToLocalTime().ToString("G");

        public string CompletedAtDisplay => Deployment.CompletedAtUtc?.ToLocalTime().ToString("G") ?? "-";

        public string DurationDisplay
        {
            get
            {
                if (Deployment.CompletedAtUtc is not { } completedAtUtc)
                {
                    return "-";
                }

                var duration = completedAtUtc - Deployment.StartedAtUtc;
                return duration.TotalDays >= 1 ?
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "{0}.{1:00}:{2:00}:{3:00}",
                        (int)duration.TotalDays,
                        duration.Hours,
                        duration.Minutes,
                        duration.Seconds) :
                    duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
            }
        }

        public string CommitSha =>
            Deployment.Version?.SourceCommitSha ?? Deployment.SourceCommitSha ?? string.Empty;

        public bool HasCommitSha => !string.IsNullOrWhiteSpace(CommitSha);

        public string StageDisplay => Deployment.Stage switch
        {
            DeploymentStage.Preparing => Texts.Preparing,
            DeploymentStage.ReadyToSwitch => Texts.ReadySwitch,
            DeploymentStage.SwitchingVersion => Texts.SwitchingVersion,
            DeploymentStage.VersionSwitched => Texts.VersionSwitched,
            DeploymentStage.UpdatingUnit => Texts.UpdatingUnitFile,
            DeploymentStage.UnitUpdated => Texts.UnitFileUpdated,
            DeploymentStage.StartingService => Texts.StartingService,
            DeploymentStage.ServiceStarted => Texts.ServiceStarted,
            DeploymentStage.HealthChecking => Texts.HealthCheck,
            DeploymentStage.Committed => Texts.OperationCompleted,
            _ => Deployment.Stage.ToString(),
        };

        public string UnitChangeDisplay => Deployment.UnitChange switch
        {
            DeploymentUnitChange.Unchanged => Texts.Unchanged,
            DeploymentUnitChange.Created => Texts.Created,
            DeploymentUnitChange.Changed => Texts.Changed,
            _ => Deployment.UnitChange.ToString(),
        };

        public string DaemonReloadDisplay =>
            Deployment.UnitChange == DeploymentUnitChange.Unchanged ?
                Texts.No :
                Texts.Yes;

        public bool HasError => !string.IsNullOrWhiteSpace(Deployment.ErrorMessage);

        public string ErrorMessage => Localize(Deployment.ErrorMessage ?? string.Empty);

        public string VariablesSnapshotDisplay => FormatJson(Deployment.VariablesSnapshotJson);

        public string LogDisplay => logText ?? Texts.OperationLogFileMissingUnavailable;

        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(RecoveryNotice));
            OnPropertyChanged(nameof(RecoveryHistoryDisplay));
            OnPropertyChanged(nameof(OperationDisplay));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StageDisplay));
            OnPropertyChanged(nameof(UnitChangeDisplay));
            OnPropertyChanged(nameof(DaemonReloadDisplay));
            OnPropertyChanged(nameof(LogDisplay));
        }

        private static string FormatJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return "{}";
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(
                    document.RootElement,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    });
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private string Localize(string key) => localizationService.Get(key);
    }
}
