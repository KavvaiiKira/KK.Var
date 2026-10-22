using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

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
            DeploymentOperationType.Deploy => Localize("Развёртывание"),
            DeploymentOperationType.Rollback => Localize("Откат"),
            _ => Deployment.OperationType.ToString(),
        };

        public string StatusDisplay => Deployment.Status switch
        {
            DeploymentStatus.Pending => Localize("Ожидает"),
            DeploymentStatus.Running => Localize("Выполняется"),
            DeploymentStatus.Succeeded => Localize("Успешно"),
            DeploymentStatus.Failed => Localize("Ошибка"),
            DeploymentStatus.Cancelled => Localize("Отменено"),
            DeploymentStatus.Interrupted => Localize("Прервано"),
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
