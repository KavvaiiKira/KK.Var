using System.Collections.Generic;
using System.Linq;
using KK.Var.Enums;

namespace KK.Var.Models;

public sealed record DeploymentPreflightResult(
    IReadOnlyList<DeploymentPreflightCheck> Checks)
{
    public bool HasErrors => Checks.Any(check => check.Status == PreflightStatus.Error);

    public bool HasWarnings => Checks.Any(check => check.Status == PreflightStatus.Warning);

    public DeploymentPreflightCheck? FirstError =>
        Checks.FirstOrDefault(check => check.Status == PreflightStatus.Error);
}
