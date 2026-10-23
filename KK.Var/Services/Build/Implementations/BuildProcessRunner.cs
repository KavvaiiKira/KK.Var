using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class BuildProcessRunner(ILocalizationService localizationService) : IBuildProcessRunner
    {
        private const string SeeAlsoPrefix = "See also";

        private const string ErrorMarker = "error:";

        private const string FailureMarker = "failed:";

        private const string NotFoundMarker = "not found";

        private const string NotRecognizedMarker = "not recognized";

        public async Task RunProcessAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (environment is not null)
            {
                foreach (var item in environment)
                {
                    if (item.Value is null)
                    {
                        startInfo.Environment.Remove(item.Key);
                    }
                    else
                    {
                        startInfo.Environment[item.Key] = item.Value;
                    }
                }
            }

            Process process;

            try
            {
                process = Process.Start(startInfo) ??
                    throw new InvalidOperationException(localizationService.Format(
                        Texts.CouldNotStart,
                        fileName));
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 2)
            {
                throw new InvalidOperationException(
                    GetMissingBuildToolMessage(fileName),
                    exception);
            }

            using var runningProcess = process;

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }

                throw;
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(localizationService.Format(
                    Texts.BuildExitedCode,
                    process.ExitCode,
                    LastUsefulLine(error, output)));
            }
        }

        private string GetMissingBuildToolMessage(string fileName) =>
            Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant() switch
            {
                BuildConstants.GoExecutable => Texts.GoSDKNotFoundInstallGoRestart,
                BuildConstants.DotNetExecutable => Texts.NETSDKNotFoundInstallNETSDK,
                BuildConstants.CmakeExecutable => Texts.CMakeNotFoundInstallCMakeRestartKK,
                _ => localizationService.Format(
                    Texts.BuildCommandNotFoundInstallRequiredTool,
                    fileName),
            };

        private string LastUsefulLine(params string[] values)
        {
            var lines = values
                .SelectMany(value => value.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray();

            var useful = lines.LastOrDefault(line =>
                !line.StartsWith(SeeAlsoPrefix, StringComparison.OrdinalIgnoreCase) &&
                (line.Contains(ErrorMarker, StringComparison.OrdinalIgnoreCase) ||
                 line.Contains(FailureMarker, StringComparison.OrdinalIgnoreCase) ||
                 line.Contains(NotFoundMarker, StringComparison.OrdinalIgnoreCase) ||
                 line.Contains(NotRecognizedMarker, StringComparison.OrdinalIgnoreCase)));

            return useful ?? lines.LastOrDefault(line =>
                    !line.StartsWith(SeeAlsoPrefix, StringComparison.OrdinalIgnoreCase)) ??
                        Texts.UnknownError;
        }
    }
}
