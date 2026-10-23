using System;
using System.IO;
using System.Threading;
using KK.Var.Models;
using Renci.SshNet;

namespace KK.Var.Services
{
    public interface IRemoteHealthCheckService
    {
        void RunHealthCheck(
            SshClient ssh,
            KKProject project,
            string userName,
            string target,
            TextWriter log,
            IProgress<DeploymentProgress>? progress,
            CancellationToken cancellationToken);
    }
}
