using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels;

public sealed class DeploymentHistoryItemViewModel(KKProjectDeployment deployment)
{
    public KKProjectDeployment Deployment { get; } = deployment;

    public string ProjectName => Deployment.Project?.Name ?? string.Empty;

    public string VersionTag => Deployment.Version?.Tag ?? string.Empty;

    public string DateDisplay =>
        Deployment.StartedAtUtc.ToLocalTime().ToString("dd/MM/yyyy, HH:mm");

    public string OperationDisplay => Deployment.OperationType switch
    {
        DeploymentOperationType.Deploy => "Deploy",
        DeploymentOperationType.Rollback => "Rollback",
        _ => Deployment.OperationType.ToString(),
    };

    public string StatusDisplay => Deployment.Status switch
    {
        DeploymentStatus.Pending => "Ожидает",
        DeploymentStatus.Running => "Выполняется",
        DeploymentStatus.Succeeded => "Успешно",
        DeploymentStatus.Failed => "Ошибка",
        DeploymentStatus.Cancelled => "Отменено",
        _ => Deployment.Status.ToString(),
    };

    public bool IsSuccessful => Deployment.Status == DeploymentStatus.Succeeded;

    public bool IsFailed => Deployment.Status == DeploymentStatus.Failed;

    public string Details => string.IsNullOrWhiteSpace(Deployment.ErrorMessage)
        ? OperationDisplay
        : Deployment.ErrorMessage;
}
