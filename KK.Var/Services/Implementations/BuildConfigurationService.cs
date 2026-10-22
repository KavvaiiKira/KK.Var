using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class BuildConfigurationService : IBuildConfigurationService
    {
        private const string ValidationSourceDirectoryName = "validation-source";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        public ProjectBuildConfiguration Read(string json)
        {
            using var document = ReadDocument(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(Texts.BuildParametersMustJSONObject);
            }

            try
            {
                return document.RootElement.Deserialize<ProjectBuildConfiguration>(JsonOptions) ??
                    throw new JsonException(Texts.BuildConfigurationEmpty);
            }
            catch (JsonException exception)
            {
                throw new JsonException(
                    Texts.BuildParametersContainInvalidJSON,
                    exception);
            }
        }

        private JsonDocument ReadDocument(string json)
        {
            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            }
            catch (JsonException exception)
            {
                throw new JsonException(
                    Texts.BuildParametersContainInvalidJSON,
                    exception);
            }
        }

        public ProjectBuildConfiguration ReadAndValidate(
            string json,
            ProjectBuildProvider provider,
            string? sourceRoot = null)
        {
            var configuration = Read(json);
            Validate(configuration, provider, sourceRoot);
            return configuration;
        }

        public string ResolveSourcePath(string root, string relativePath)
        {
            var value = relativePath.Replace(BuildConstants.SourceUnixPrefix, string.Empty, StringComparison.Ordinal)
                .Replace(BuildConstants.SourceWindowsPrefix, string.Empty, StringComparison.Ordinal);
            if (value == BuildConstants.SourcePlaceholder)
            {
                value = ".";
            }

            if (Path.IsPathRooted(value) || value.Contains(':') || value.Any(char.IsControl))
            {
                throw new InvalidDataException(Texts.BuildPathsMustStayInsideSourceDirectory);
            }

            var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(rootPath, value.Replace('/', Path.DirectorySeparatorChar)));
            if (path != rootPath && !path.StartsWith(
                rootPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(Texts.BuildPathsMustStayInsideSourceDirectory);
            }

            var current = path;
            while (current.Length >= rootPath.Length)
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(Texts.BuildPathsMustStayInsideSourceDirectory);
                }

                if (string.Equals(current, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = Path.GetDirectoryName(current) ?? rootPath;
            }

            return path;
        }

        public void Validate(
            ProjectBuildConfiguration configuration,
            ProjectBuildProvider provider,
            string? sourceRoot = null)
        {
            var root = sourceRoot ?? Path.Combine(Path.GetTempPath(), ApplicationConstants.Name, ValidationSourceDirectoryName);
            var working = ResolveSourcePath(root, configuration.WorkingDirectory ?? ".");
            var files = provider switch
            {
                ProjectBuildProvider.DotNet => new[] { configuration.DotNetProjectPath },
                ProjectBuildProvider.Cpp => new[] { configuration.ToolchainFile },
                ProjectBuildProvider.Python => new[] { configuration.PythonDependencyFilePath },
                _ => Array.Empty<string?>(),
            };
            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file))
                {
                    var path = ResolveSourcePath(
                        working,
                        file.Replace(BuildConstants.ArchitecturePlaceholder, ArchitectureNames.X8664)
                                            .Replace(BuildConstants.RuntimePlaceholder, ArchitectureNames.LinuxX64Runtime));
                    if (sourceRoot is not null && !file.Contains('{') && !File.Exists(path))
                    {
                        throw new InvalidDataException(Texts.SpecifiedBuildFileNotFound);
                    }
                }
            }

            if (provider == ProjectBuildProvider.DotNet &&
                !string.IsNullOrWhiteSpace(configuration.DotNetProjectPath) &&
                !new[] { BuildConstants.DotNetProjectExtension, BuildConstants.DotNetSolutionExtension, BuildConstants.DotNetSolutionXmlExtension }.Contains(
                    Path.GetExtension(configuration.DotNetProjectPath),
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(Texts.SelectCsprojSlnSlnxFile);
            }

            if (provider == ProjectBuildProvider.Python &&
                !string.IsNullOrWhiteSpace(configuration.PythonDependencyFilePath) &&
                !configuration.PythonDependencyFilePath.EndsWith(BuildConstants.TextFileExtension, StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(configuration.PythonDependencyFilePath).Equals(
                    BuildConstants.PythonProjectFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(Texts.SpecifyRequirementsTxtFilePyprojectToml);
            }

            if (sourceRoot is not null && !Directory.Exists(working))
            {
                throw new InvalidDataException(Texts.BuildWorkingDirectoryNotFound);
            }

            if (provider == ProjectBuildProvider.Custom && string.IsNullOrWhiteSpace(configuration.Command))
            {
                throw new InvalidDataException(Texts.SpecifyCustomBuildCommand);
            }

            if (provider == ProjectBuildProvider.Cpp && string.IsNullOrWhiteSpace(configuration.ToolchainFile))
            {
                throw new InvalidDataException(Texts.EnterToolchainFileCLinuxBuild);
            }

            if ((configuration.BuildArguments ?? []).Concat(configuration.ConfigureArguments ?? [])
                .Any(argument => string.IsNullOrWhiteSpace(argument) || argument.Contains('\0')) ||
                (configuration.Environment ?? []).Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
                    pair.Key.Contains('=') || pair.Key.Any(char.IsControl) || pair.Value?.Contains('\0') == true))
            {
                throw new InvalidDataException(Texts.InvalidBuildArgumentsBuildProcessEnvironment);
            }

            if (provider == ProjectBuildProvider.Python &&
                configuration.PythonDependencyFilePath?.Contains('{') == true)
            {
                throw new InvalidDataException(Texts.PythonSpecifyRelativePathWithoutPlaceholders);
            }
        }

        public ProjectBuildProvider DetectProvider(string directory)
        {
            var providers = new List<ProjectBuildProvider>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            };
            if (Directory.EnumerateFiles(directory, BuildConstants.DotNetProjectPattern, options).Any() ||
                Directory.EnumerateFiles(directory, BuildConstants.DotNetSolutionPattern, SearchOption.TopDirectoryOnly).Any())
            {
                providers.Add(ProjectBuildProvider.DotNet);
            }
            if (File.Exists(Path.Combine(directory, BuildConstants.GoModuleFileName)))
            {
                providers.Add(ProjectBuildProvider.Go);
            }
            if (File.Exists(Path.Combine(directory, BuildConstants.PythonProjectFileName)) ||
                File.Exists(Path.Combine(directory, BuildConstants.PythonRequirementsFileName)) ||
                Directory.EnumerateFiles(directory, BuildConstants.PythonSourcePattern).Any())
            {
                providers.Add(ProjectBuildProvider.Python);
            }
            if (File.Exists(Path.Combine(directory, BuildConstants.CmakeProjectFileName)) ||
                Directory.EnumerateFiles(directory, BuildConstants.CppSourcePattern, options).Any())
            {
                providers.Add(ProjectBuildProvider.Cpp);
            }
            return providers.Count switch
            {
                1 => providers[0],
                0 => throw new InvalidDataException(Texts.ProjectBuildTypeCouldNotDetectedAutomatically),
                _ => throw new InvalidDataException(Texts.MultipleBuildTypesDetectedSelectCorrectOne),
            };
        }
    }
}
