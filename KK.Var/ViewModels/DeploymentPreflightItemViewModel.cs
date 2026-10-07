using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels;

public sealed class DeploymentPreflightItemViewModel(DeploymentPreflightCheck check)
{
    public string Message => check.Message;

    public string? Detail => check.Detail;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public bool IsSuccess => check.Status == PreflightStatus.Success;

    public bool IsWarning => check.Status == PreflightStatus.Warning;

    public bool IsError => check.Status == PreflightStatus.Error;
}
