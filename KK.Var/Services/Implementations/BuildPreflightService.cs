using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services.Implementations
{
    public sealed class BuildPreflightService(
        IBuildConfigurationService buildConfigurationService,
        IPreflightCheckFactory checkFactory,
        ILocalizationService localizationService) : IBuildPreflightService
    {
        public async Task CheckBuildConfigurationAsync(
            KKProject project,
            List<DeploymentPreflightCheck> checks,
            CancellationToken cancellationToken)
        {
            try
            {
                var configuration = buildConfigurationService.Read(project.BuildConfigurationJson);

                if (configuration.BuildArguments is null ||
                    configuration.ConfigureArguments is null ||
                    configuration.Environment is null ||
                    configuration.BuildArguments.Any(argument => argument is null) ||
                    configuration.ConfigureArguments.Any(argument => argument is null))
                {
                    throw new InvalidDataException(localizationService.Get("Аргументы сборки или переменные окружения недопустимы."));
                }

                var provider = project.BuildProvider;
                if (provider == ProjectBuildProvider.Unknown &&
                    project.SourceType == ProjectSourceType.LocalDirectory &&
                    !string.IsNullOrWhiteSpace(project.LocalDirectoryPath) &&
                    Directory.Exists(project.LocalDirectoryPath))
                {
                    provider = DetectProvider(buildConfigurationService.ResolveSourcePath(
                        project.LocalDirectoryPath,
                        configuration.WorkingDirectory ?? "."));
                }

                var sourceRoot = project.SourceType == ProjectSourceType.LocalDirectory ?
                    project.LocalDirectoryPath :
                    null;
                buildConfigurationService.Validate(configuration, provider, sourceRoot);

                if (provider == ProjectBuildProvider.Unknown)
                {
                    checks.Add(checkFactory.Warning(
                        PreflightCheckKeys.Build,
                        "Способ сборки будет определён после загрузки исходников."));
                    return;
                }

                if (sourceRoot is null)
                {
                    checks.Add(checkFactory.Warning(
                        PreflightCheckKeys.BuildSource,
                        "Входные файлы сборки будут проверены после загрузки GitHub-архива."));
                }

                if (sourceRoot is not null && !Directory.Exists(sourceRoot))
                {
                    return;
                }

                if (sourceRoot is not null)
                {
                    var buildRoot = buildConfigurationService.ResolveSourcePath(
                        sourceRoot,
                        configuration.WorkingDirectory ?? ".");
                    var hasInputs = provider switch
                    {
                        ProjectBuildProvider.DotNet =>
                            !string.IsNullOrWhiteSpace(configuration.DotNetProjectPath) ||
                            Directory.EnumerateFiles(
                                buildRoot,
                                BuildConstants.DotNetProjectPattern,
                                SearchOption.AllDirectories).Any(),
                        ProjectBuildProvider.Go =>
                            File.Exists(Path.Combine(buildRoot, BuildConstants.GoModuleFileName)),
                        ProjectBuildProvider.Python =>
                            File.Exists(Path.Combine(buildRoot,
                                project.RemoteExecutableFileName.Replace(
                                    '/',
                                    Path.DirectorySeparatorChar))),
                        ProjectBuildProvider.Cpp =>
                            File.Exists(Path.Combine(buildRoot, BuildConstants.CmakeProjectFileName)),
                        _ => true,
                    };
                    if (!hasInputs)
                    {
                        throw new FileNotFoundException(localizationService.Get(
                            "Не найдены входные файлы выбранного способа сборки."));
                    }
                }

                if (provider == ProjectBuildProvider.Custom &&
                    string.IsNullOrWhiteSpace(configuration.Command))
                {
                    throw new InvalidDataException(localizationService.Get(
                        "Укажите команду пользовательской сборки."));
                }

                if (provider == ProjectBuildProvider.Cpp &&
                    string.IsNullOrWhiteSpace(configuration.ToolchainFile))
                {
                    throw new InvalidDataException(localizationService.Get(
                        "Укажите существующий CMake toolchain-файл для сборки C++ под Linux."));
                }

                if (provider == ProjectBuildProvider.Cpp &&
                    sourceRoot is not null &&
                    !string.IsNullOrWhiteSpace(configuration.ToolchainFile))
                {
                    var toolchain = configuration.ToolchainFile.Replace(
                        BuildConstants.SourcePlaceholder,
                        sourceRoot,
                        StringComparison.Ordinal);
                    if (toolchain.Contains('{'))
                    {
                        checks.Add(checkFactory.Warning(
                            PreflightCheckKeys.Toolchain,
                            "Путь к toolchain будет проверен после подстановки параметров сборки."));
                    }

                    if (!toolchain.Contains('{') &&
                        !File.Exists(Path.GetFullPath(Path.IsPathRooted(toolchain) ?
                            toolchain :
                            Path.Combine(sourceRoot, toolchain))))
                    {
                        throw new FileNotFoundException(toolchain);
                    }
                }

                var tool = provider switch
                {
                    ProjectBuildProvider.DotNet => BuildConstants.DotNetExecutable,
                    ProjectBuildProvider.Go => BuildConstants.GoExecutable,
                    ProjectBuildProvider.Cpp => BuildConstants.CmakeExecutable,
                    ProjectBuildProvider.Custom => configuration.Command,
                    _ => null,
                };

                if (tool?.Contains('{') == true)
                {
                    checks.Add(checkFactory.Warning(
                        PreflightCheckKeys.BuildTool,
                        "Команда сборки будет проверена после подстановки параметров."));
                }

                if (tool is not null &&
                    !tool.Contains('{') &&
                    !ToolExists(tool, sourceRoot))
                {
                    throw new FileNotFoundException(localizationService.Format(
                        "Инструмент сборки «{0}» не найден.",
                        tool));
                }

                if (provider == ProjectBuildProvider.DotNet &&
                    !await HasDotNetSdkAsync(cancellationToken))
                {
                    throw new InvalidOperationException(localizationService.Get(
                        ".NET SDK не найден. Установите .NET SDK и перезапустите KK.Var."));
                }

                if (provider == ProjectBuildProvider.Cpp &&
                    (string.IsNullOrWhiteSpace(configuration.CmakeGenerator) ||
                     configuration.CmakeGenerator.Equals(BuildConstants.NinjaGenerator, StringComparison.OrdinalIgnoreCase)) &&
                    !ToolExists(BuildConstants.NinjaExecutable))
                {
                    throw new FileNotFoundException(localizationService.Get(
                        "Ninja не найден."));
                }

                checks.Add(checkFactory.Success(PreflightCheckKeys.Build, "Build-конфигурация и локальный инструмент проверены."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.Build,
                    "Build-конфигурация или инструмент недоступны.",
                    exception.Message));
            }
        }

        private ProjectBuildProvider DetectProvider(string sourceDirectory) =>
            buildConfigurationService.DetectProvider(sourceDirectory);

        private static bool ToolExists(string tool, string? sourceRoot = null)
        {
            if (Path.IsPathFullyQualified(tool))
            {
                return File.Exists(tool);
            }

            if (tool.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            {
                return File.Exists(Path.GetFullPath(Path.Combine(
                    sourceRoot ?? Directory.GetCurrentDirectory(),
                    tool)));
            }

            var extensions = OperatingSystem.IsWindows() ?
                new[] { BuildConstants.WindowsExecutableExtension, BuildConstants.WindowsCommandExtension, BuildConstants.WindowsBatchExtension, string.Empty } :
                new[] { string.Empty };
            foreach (var directory in (Environment.GetEnvironmentVariable(BuildConstants.PathVariable) ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var extension in extensions)
                {
                    if (File.Exists(Path.Combine(directory.Trim('"'), tool + extension)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task<bool> HasDotNetSdkAsync(CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = BuildConstants.DotNetExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(BuildConstants.DotNetListSdksOption);

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException(localizationService.Get("Не удалось запустить процесс dotnet."));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var output = await outputTask;
                _ = await errorTask;
                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
        }
    }
}
