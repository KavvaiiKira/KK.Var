using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;
using Renci.SshNet;

namespace KK.Var.Services
{
    public interface IRemoteRecoveryService
    {
        Task<DeploymentRecoveryResult> RecoverAsync(
            KKProject project,
            RemoteMachineSettings settings,
            string operationId,
            DeploymentStage stage,
            DeploymentUnitChange unitChange,
            TextWriter log,
            CancellationToken cancellationToken = default);

        string RestorePreviousVersion(
            SshClient ssh,
            KKProject project,
            string target,
            string backup,
            string unitPath,
            string unitBackup,
            DeploymentUnitChange unitState,
            TextWriter log,
            IProgress<DeploymentProgress>? progress);

        void CleanupRemoteOperationFiles(
            SshClient ssh,
            string staging,
            string remoteArchive,
            string remoteUnit,
            string unitBackup,
            TextWriter log);

        void CleanupCommittedDeploymentFiles(
            SshClient ssh,
            string backup,
            string remoteArchive,
            string remoteUnit,
            string unitBackup,
            string marker,
            TextWriter log);
    }
}
