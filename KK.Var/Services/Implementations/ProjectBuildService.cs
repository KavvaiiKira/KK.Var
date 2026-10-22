using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using static KK.Var.Services.SourceFiles;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class ProjectBuildService(
        IBuildConfigurationService buildConfigurationService,
        IBuildProcessRunner processRunner,
        ILocalizationService localizationService) : IProjectBuildService
    {
        private const string SelfContainedEnabledArgument = "true";

        public async Task BuildAsync(
            ProjectBuildProvider provider,
            KKProject project,
            ProjectBuildConfiguration configuration,
            string sourceDirectory,
            string outputDirectory,
            string remoteArchitecture,
            CancellationToken cancellationToken)
        {
            var buildArguments = configuration.BuildArguments ?? [];
            var configureArguments = configuration.ConfigureArguments ?? [];
            var environment = configuration.Environment ?? [];

            switch (provider)
            {
                case ProjectBuildProvider.DotNet:
                    {
                        var target = string.IsNullOrWhiteSpace(configuration.DotNetProjectPath) ?
                            SelectDotNetProject(sourceDirectory, project.RemoteExecutableFileName) :
                            buildConfigurationService.ResolveSourcePath(
                                sourceDirectory,
                                configuration.DotNetProjectPath);
                        var rid = MapDotNetRuntime(remoteArchitecture);
                        var arguments = new List<string>
                        {
                            BuildConstants.DotNetPublishCommand,
                            target,
                            BuildConstants.DotNetConfigurationOption,
                            string.IsNullOrWhiteSpace(configuration.Configuration) ?
                                BuildConstants.ReleaseConfiguration :
                                configuration.Configuration.Trim(),
                            BuildConstants.DotNetRuntimeOption,
                            rid,
                            BuildConstants.SelfContainedOption,
                            SelfContainedEnabledArgument,
                            BuildConstants.OutputOption,
                            outputDirectory,
                        };
                        arguments.AddRange(buildArguments);

                        await processRunner.RunProcessAsync(
                            BuildConstants.DotNetExecutable,
                            arguments,
                            sourceDirectory,
                            environment,
                            cancellationToken);

                        break;
                    }
                case ProjectBuildProvider.Go:
                    {
                        var goArch = MapGoArchitecture(remoteArchitecture);

                        var goEnvironment = new Dictionary<string, string?>(
                            environment,
                            StringComparer.Ordinal)
                        {
                            [BuildConstants.GoOperatingSystemVariable] = BuildConstants.LinuxOperatingSystem,
                            [BuildConstants.GoArchitectureVariable] = goArch,
                            [BuildConstants.CgoEnabledVariable] = configuration.CgoEnabled ? "1" : "0",
                        };

                        var goOutputPath = Path.Combine(
                            outputDirectory,
                            project.RemoteExecutableFileName);

                        Directory.CreateDirectory(Path.GetDirectoryName(goOutputPath)!);

                        var arguments = new List<string> { BuildConstants.GoBuildCommand };

                        arguments.AddRange(buildArguments);
                        arguments.AddRange(
                            [BuildConstants.OutputOption, goOutputPath, "."]);

                        await processRunner.RunProcessAsync(
                            BuildConstants.GoExecutable,
                            arguments,
                            sourceDirectory,
                            goEnvironment,
                            cancellationToken);

                        break;
                    }
                case ProjectBuildProvider.Python:
                    CopyDirectory(sourceDirectory, outputDirectory, cancellationToken);
                    break;
                case ProjectBuildProvider.Cpp:
                    {
                        var runtime = MapLinuxRuntime(remoteArchitecture);

                        var configuredToolchain = (configuration.ToolchainFile ?? string.Empty)
                            .Replace(BuildConstants.ArchitecturePlaceholder, remoteArchitecture, StringComparison.Ordinal)
                            .Replace(BuildConstants.RuntimePlaceholder, runtime, StringComparison.Ordinal);

                        var toolchainFile = buildConfigurationService.ResolveSourcePath(
                            sourceDirectory,
                            configuredToolchain);

                        if (string.IsNullOrWhiteSpace(configuredToolchain) ||
                            !File.Exists(toolchainFile))
                        {
                            throw new FileNotFoundException(Texts.SpecifyExistingCMakeToolchainFileCLinux);
                        }

                        var buildDirectory = Path.Combine(
                            Path.GetDirectoryName(outputDirectory)!,
                            BuildConstants.CmakeBuildDirectoryName);

                        var buildConfiguration =
                            string.IsNullOrWhiteSpace(configuration.Configuration) ?
                                BuildConstants.ReleaseConfiguration :
                                configuration.Configuration.Trim();

                        var configure = new List<string>
                        {
                            BuildConstants.CmakeSourceOption,
                            sourceDirectory,
                            BuildConstants.CmakeBuildDirectoryOption,
                            buildDirectory,
                            BuildConstants.CmakeGeneratorOption,
                            string.IsNullOrWhiteSpace(configuration.CmakeGenerator) ?
                                BuildConstants.NinjaGenerator :
                                configuration.CmakeGenerator.Trim(),
                            $"{BuildConstants.CmakeBuildTypePrefix}{buildConfiguration}",
                            $"{BuildConstants.CmakeToolchainPrefix}{toolchainFile}",
                            $"{BuildConstants.CmakeOutputDirectoryPrefix}{outputDirectory}",
                            $"{BuildConstants.CmakeTargetArchitecturePrefix}{remoteArchitecture}",
                        };
                        configure.AddRange(configureArguments);

                        await processRunner.RunProcessAsync(
                            BuildConstants.CmakeExecutable,
                            configure,
                            sourceDirectory,
                            environment,
                            cancellationToken);

                        var build = new List<string>
                        {
                            BuildConstants.CmakeBuildOption,
                            buildDirectory,
                            BuildConstants.CmakeConfigurationOption,
                            buildConfiguration,
                        };
                        build.AddRange(buildArguments);

                        await processRunner.RunProcessAsync(
                            BuildConstants.CmakeExecutable,
                            build,
                            sourceDirectory,
                            environment,
                            cancellationToken);

                        break;
                    }
                case ProjectBuildProvider.Custom:
                    {
                        if (string.IsNullOrWhiteSpace(configuration.Command))
                        {
                            throw new InvalidOperationException(Texts.SpecifyCustomBuildCommand);
                        }

                        var runtime = MapLinuxRuntime(remoteArchitecture);

                        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [BuildConstants.SourcePlaceholder] = sourceDirectory,
                            [BuildConstants.OutputPlaceholder] = outputDirectory,
                            [BuildConstants.RuntimePlaceholder] = runtime,
                            [BuildConstants.ArchitecturePlaceholder] = remoteArchitecture,
                        };

                        var customEnvironment = new Dictionary<string, string?>(
                            environment,
                            StringComparer.Ordinal)
                        {
                            [BuildConstants.SourceDirectoryVariable] = sourceDirectory,
                            [BuildConstants.OutputDirectoryVariable] = outputDirectory,
                            [BuildConstants.RuntimeVariable] = runtime,
                            [BuildConstants.ArchitectureVariable] = remoteArchitecture,
                        };

                        await processRunner.RunProcessAsync(
                            ReplacePlaceholders(configuration.Command, replacements),
                            buildArguments
                                                        .Select(argument => ReplacePlaceholders(argument, replacements))
                                                        .ToArray(),
                            sourceDirectory,
                            customEnvironment,
                            cancellationToken);

                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider));
            }
        }

        private string MapDotNetRuntime(string architecture) => architecture switch
        {
            ArchitectureNames.X8664 or ArchitectureNames.Amd64 => ArchitectureNames.LinuxX64Runtime,
            ArchitectureNames.Aarch64 or ArchitectureNames.Arm64 => ArchitectureNames.LinuxArm64Runtime,
            _ => throw new NotSupportedException(localizationService.Format(
                Texts.RemoteArchitectureNotCurrentlySupportedNET,
                architecture)),
        };

        private string MapGoArchitecture(string architecture) => architecture switch
        {
            ArchitectureNames.X8664 or ArchitectureNames.Amd64 => ArchitectureNames.Amd64,
            ArchitectureNames.Aarch64 or ArchitectureNames.Arm64 => ArchitectureNames.Arm64,
            _ => throw new NotSupportedException(localizationService.Format(
                Texts.RemoteArchitectureNotCurrentlySupportedGo,
                architecture)),
        };

        private string MapLinuxRuntime(string architecture) => architecture switch
        {
            ArchitectureNames.X8664 or ArchitectureNames.Amd64 => ArchitectureNames.LinuxX64Runtime,
            ArchitectureNames.Aarch64 or ArchitectureNames.Arm64 => ArchitectureNames.LinuxArm64Runtime,
            _ => throw new NotSupportedException(localizationService.Format(
                Texts.RemoteArchitectureNotCurrentlySupported,
                architecture)),
        };

        private static string ReplacePlaceholders(
            string value,
            IReadOnlyDictionary<string, string> replacements)
        {
            var result = value;

            foreach (var replacement in replacements)
            {
                result = result.Replace(
                    replacement.Key,
                    replacement.Value,
                    StringComparison.Ordinal);
            }

            return result;
        }

        private string SelectDotNetProject(
            string sourceDirectory,
            string executableFileName)
        {
            var projects = Directory
                .EnumerateFiles(sourceDirectory, BuildConstants.DotNetProjectPattern, SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = path,
                    Document = XDocument.Load(path),
                })
                .Where(item =>
                {
                    var outputType = item.Document
                        .Descendants()
                        .FirstOrDefault(element => element.Name.LocalName == BuildConstants.DotNetOutputTypeProperty)
                        ?.Value;
                    return outputType is not null &&
                           (outputType.Equals(BuildConstants.DotNetConsoleOutputType, StringComparison.OrdinalIgnoreCase) ||
                            outputType.Equals(BuildConstants.DotNetWindowsOutputType, StringComparison.OrdinalIgnoreCase));
                })
                .ToArray();

            var executableName = Path.GetFileNameWithoutExtension(executableFileName);

            var matching = projects.Where(item =>
            {
                var assemblyName = item.Document
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == BuildConstants.DotNetAssemblyNameProperty)
                    ?.Value ?? Path.GetFileNameWithoutExtension(item.Path);
                return assemblyName.Equals(executableName, StringComparison.OrdinalIgnoreCase);
            }).ToArray();

            return matching.Length switch
            {
                1 => matching[0].Path,
                0 when projects.Length == 1 => projects[0].Path,
                0 => throw new InvalidOperationException(Texts.ExecutableCsprojCouldNotSelectedUnambiguouslyAssembly),
                _ => throw new InvalidOperationException(Texts.MultipleExecutableCsprojFilesSameAssemblyName),
            };
        }
    }
}
