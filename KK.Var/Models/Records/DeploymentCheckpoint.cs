using KK.Var.Enums;

namespace KK.Var.Models;

public sealed record DeploymentCheckpoint(
    DeploymentStage Stage,
    DeploymentUnitChange UnitChange);
