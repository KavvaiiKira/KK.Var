using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public sealed class DeploymentHistoryItemViewModel(
        KKProjectDeployment deployment,
        ILocalizationService localizationService) : ViewModelBase
    {
        public KKProjectDeployment Deployment { get; } = deployment;

        public string ProjectName => Deployment.Project?.Name ?? string.Empty;

        public string MachineDisplay => $"{Deployment.RemoteMachineName} · {Deployment.RemoteHost}:{Deployment.RemotePort}";

        public string VersionTag => Deployment.Version?.Tag ?? Deployment.VersionTag;

        public string DateDisplay => Deployment.StartedAtUtc.ToLocalTime().ToString("g");

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

        public bool IsSuccessful => Deployment.Status == DeploymentStatus.Succeeded;

        public bool IsFailed => Deployment.Status is DeploymentStatus.Failed or DeploymentStatus.Interrupted;

        public string Details =>
            string.IsNullOrWhiteSpace(Deployment.ErrorMessage) ?
                OperationDisplay :
                Localize(Deployment.ErrorMessage);

        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(DateDisplay));
            OnPropertyChanged(nameof(OperationDisplay));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(Details));
        }

        private string Localize(string key) => localizationService.Get(key);
    }
}
