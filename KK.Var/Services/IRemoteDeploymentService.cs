using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IRemoteDeploymentService
{
    Task DeployAsync(
        KKProject project,
        string artifactPath,
        RemoteMachineSettings settings,
        string operationId,
        TextWriter log,
        Func<DeploymentCheckpoint, Task> checkpoint,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DeploymentRecoveryResult> RecoverAsync(
        KKProject project,
        RemoteMachineSettings settings,
        string operationId,
        DeploymentStage stage,
        DeploymentUnitChange unitChange,
        TextWriter log,
        CancellationToken cancellationToken = default);
}
