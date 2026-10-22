namespace KK.Var.Models
{
    public sealed record ApplicationStartupResult(int RecoveredDeploymentCount, string? RecoveryError);
}
