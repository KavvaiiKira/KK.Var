using KK.Var.Enums;

namespace KK.Var.Models;

public sealed record DeploymentPreflightCheck(
    string Key,
    PreflightStatus Status,
    string Message,
    string? Detail = null);
