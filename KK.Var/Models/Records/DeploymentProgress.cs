namespace KK.Var.Models;

public sealed record DeploymentProgress(
    int Percentage,
    string Message,
    string? LogLine = null);
