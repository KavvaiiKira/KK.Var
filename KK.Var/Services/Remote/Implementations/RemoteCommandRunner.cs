using System;
using System.IO;
using System.Linq;
using KK.Var.Models;
using Renci.SshNet;
using static KK.Var.Services.RemoteShell;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteCommandRunner(ILocalizationService localizationService) : IRemoteCommandRunner
    {
        private const string MissingEnsurePipMessage = "ensurepip is not available";

        private const string JournalOutputOptions = "--no-pager --output=short-iso";

        public void CaptureFailureDiagnostics(
            SshClient ssh,
            KKProject project,
            TextWriter log)
        {
            log.WriteLine(Texts.NewVersionDiagnosticsAutomaticRollback);

            try
            {
                Run(
                    ssh,
                    $"sudo systemctl status --no-pager --full -- {Q(project.RemoteServiceName)}",
                    log);
                Run(
                    ssh,
                    $"sudo journalctl --unit {Q(project.RemoteServiceName)} --lines 200 " +
                                    JournalOutputOptions,
                    log);
            }
            catch (Exception exception)
            {
                log.WriteLine(localizationService.Format(
                    Texts.CouldNotCollectAllDiagnostics,
                    exception));
            }
        }

        public RemoteCommandResult Run(
            SshClient client,
            string command,
            TextWriter log,
            TimeSpan? commandTimeout = null)
        {
            log.WriteLine($"> {command}");

            using var result = client.CreateCommand(command);

            if (commandTimeout.HasValue)
            {
                result.CommandTimeout = commandTimeout.Value;
            }

            result.Execute();

            if (!string.IsNullOrWhiteSpace(result.Result))
            {
                log.WriteLine(result.Result.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                log.WriteLine(result.Error.TrimEnd());
            }

            return new RemoteCommandResult(result.ExitStatus, result.Result, result.Error);
        }

        public void RunChecked(SshClient client, string command, TextWriter log)
        {
            var result = Run(client, command, log);

            if (result.ExitStatus != 0)
            {
                var output = string.IsNullOrWhiteSpace(result.Error) ?
                    result.Result :
                    result.Error;

                var message = output.Contains(
                    MissingEnsurePipMessage,
                    StringComparison.OrdinalIgnoreCase) ?
                        Texts.CouldNotCreatePythonVirtualEnvironmentVenv :
                        string.IsNullOrWhiteSpace(output) ?
                            localizationService.Format(
                                Texts.RemoteCommandExitedCode,
                                result.ExitStatus) :
                            LastUsefulLine(output);
                throw new InvalidOperationException(message);
            }
        }

        private static string LastUsefulLine(string output) => output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;

        public string RunCheckedWithOutput(
            SshClient client,
            string command,
            TextWriter log)
        {
            var result = Run(client, command, log);
            if (result.ExitStatus != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error) ?
                        localizationService.Format(
                            Texts.RemoteCommandExitedCode,
                            result.ExitStatus) :
                        result.Error.Trim());
            }
            return result.Result.Trim();
        }
    }
}
