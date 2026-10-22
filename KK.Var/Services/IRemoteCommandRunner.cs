using System;
using System.IO;
using KK.Var.Models;
using Renci.SshNet;

namespace KK.Var.Services
{
    public interface IRemoteCommandRunner
    {
        RemoteCommandResult Run(
            SshClient client,
            string command,
            TextWriter log,
            TimeSpan? commandTimeout = null);

        void RunChecked(SshClient client, string command, TextWriter log);

        string RunCheckedWithOutput(
            SshClient client,
            string command,
            TextWriter log);

        void CaptureFailureDiagnostics(
            SshClient ssh,
            KKProject project,
            TextWriter log);
    }
}
