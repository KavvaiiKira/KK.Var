using System;

namespace KK.Var.Models
{
    public sealed record DeploymentTargetSnapshot(
        Guid RemoteMachineId,
        string ServiceName,
        string Directory,
        string Executable);
}
