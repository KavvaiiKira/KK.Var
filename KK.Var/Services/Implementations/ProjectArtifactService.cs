using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services.Implementations;

public sealed class ProjectArtifactService(
    IKKProjectEnvironmentService environmentService,
    IGitHubService gitHubService,
    IGitHubAuthenticationService gitHubAuthenticationService,
    ILocalizationService localizationService) : IProjectArtifactService
{
    private static readonly Regex TagPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> IgnoredDirectories = new(
        [".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", ".venv"],
        StringComparer.OrdinalIgnoreCase);

    public Task DeleteAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        var root = Path.GetFullPath(DatabasePaths.ArtifactsDirectory);
        var projectDirectory = Path.GetFullPath(Path.Combine(
            root,
            projectId.ToString("N")));
        var expectedParent = Directory.GetParent(projectDirectory)?.FullName;
        if (!string.Equals(expectedParent, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Project artifact path is outside the artifact root.");
        }

        if (!Directory.Exists(projectDirectory))
        {
            return Task.CompletedTask;
        }

        var directory = new DirectoryInfo(projectDirectory);
        EnsureNoReparsePoints(directory);

        Directory.Delete(projectDirectory, recursive: true);
        return Task.CompletedTask;
    }

    private static void EnsureNoReparsePoints(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Project artifact directory cannot contain reparse points.");
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Project artifact directory cannot contain reparse points.");
            }

            if (entry is DirectoryInfo childDirectory)
            {
                EnsureNoReparsePoints(childDirectory);
            }
        }
    }

    public async Task<ProjectArtifact> CreateAsync(
        KKProject project,
        string versionTag,
        string remoteArchitecture,
        IProgress<DeploymentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var tag = versionTag.Trim();
        if (!TagPattern.IsMatch(tag))
        {
            throw new ArgumentException(localizationService.Get(
                "Тег версии может содержать только латинские буквы, цифры, точку, дефис и подчёркивание."));
        }

        var operationDirectory = Path.Combine(
            Path.GetTempPath(),
            "KK.Var",
            Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(operationDirectory, "source");
        var outputDirectory = Path.Combine(operationDirectory, "output");
        string? artifactPath = null;

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            progress?.Report(new DeploymentProgress(
                5,
                localizationService.Get("Подготовка исходного кода")));
            var sourceCommitSha = await AcquireSourceAsync(
                project,
                sourceDirectory,
                cancellationToken);

            var configuration = JsonSerializer.Deserialize<ProjectBuildConfiguration>(
                project.BuildConfigurationJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ProjectBuildConfiguration();
            var buildSourceDirectory = ResolveBuildSourceDirectory(
                configuration.WorkingDirectory,
                sourceDirectory);
            var provider = DetectProvider(project, buildSourceDirectory);
            progress?.Report(new DeploymentProgress(
                15,
                localizationService.Format(
                    "Сборка: {0}",
                    FormatProvider(provider))));
            await BuildAsync(
                provider,
                project,
                configuration,
                buildSourceDirectory,
                outputDirectory,
                remoteArchitecture,
                cancellationToken);

            await environmentService.WriteFileAsync(
                project.Id,
                outputDirectory,
                cancellationToken);

            var executablePath = Path.GetFullPath(Path.Combine(
                outputDirectory,
                GetBuiltExecutableRelativePath(project, provider)
                    .Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(executablePath))
            {
                throw new InvalidOperationException(localizationService.Format(
                    "После сборки не найден исполняемый файл «{0}».",
                    project.RemoteExecutableFileName));
            }

            progress?.Report(new DeploymentProgress(
                45,
                localizationService.Get("Создание локального архива")));
            var projectDirectory = Path.Combine(
                DatabasePaths.ArtifactsDirectory,
                project.Id.ToString("N"));
            Directory.CreateDirectory(projectDirectory);
            var relativePath = Path.Combine(
                project.Id.ToString("N"),
                $"{tag}.tar.gz");
            artifactPath = Path.Combine(DatabasePaths.ArtifactsDirectory, relativePath);

            if (File.Exists(artifactPath))
            {
                throw new InvalidOperationException(localizationService.Format(
                    "Артефакт версии «{0}» уже существует.",
                    tag));
            }

            await CreateTarGzAsync(outputDirectory, artifactPath, cancellationToken);
            var hash = await CalculateSha256Async(artifactPath, cancellationToken);
            var size = new FileInfo(artifactPath).Length;

            return new ProjectArtifact(
                artifactPath,
                relativePath.Replace('\\', '/'),
                hash,
                size,
                sourceCommitSha,
                provider);
        }
        catch
        {
            if (artifactPath is not null && File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(operationDirectory))
            {
                Directory.Delete(operationDirectory, recursive: true);
            }
        }
    }

    private async Task<string?> AcquireSourceAsync(
        KKProject project,
        string destination,
        CancellationToken cancellationToken)
    {
        if (project.SourceType == ProjectSourceType.LocalDirectory)
        {
            var source = project.LocalDirectoryPath
                ?? throw new InvalidOperationException(localizationService.Get(
                    "Не указана локальная папка проекта."));
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(localizationService.Format(
                    "Локальная папка проекта не найдена: {0}",
                    source));
            }

            await Task.Run(
                () => CopyDirectory(source, destination, cancellationToken),
                cancellationToken);
            return null;
        }

        var repository = project.GitHubRepositoryFullName
            ?? throw new InvalidOperationException(localizationService.Get(
                "Не указан репозиторий GitHub."));
        var token = await gitHubAuthenticationService.GetTokenAsync(cancellationToken);
        if (token is null)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Подключите GitHub в настройках перед сборкой проекта."));
        }

        var commitSha = await gitHubService.GetDefaultBranchCommitShaAsync(
            repository,
            token.AccessToken,
            cancellationToken);
        await using var archive = await gitHubService.DownloadRepositoryArchiveAsync(
            repository,
            commitSha,
            token.AccessToken,
            cancellationToken);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        ExtractGitHubArchive(zip, destination);
        await PopulateGitHubSubmodulesAsync(
            repository,
            commitSha,
            destination,
            token.AccessToken,
            0,
            cancellationToken);
        return commitSha;
    }

    private async Task PopulateGitHubSubmodulesAsync(
        string repositoryFullName,
        string commitSha,
        string sourceDirectory,
        string accessToken,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth >= 8)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Слишком большая вложенность Git submodule."));
        }

        var submodules = await gitHubService.GetSubmodulesAsync(
            repositoryFullName,
            commitSha,
            accessToken,
            cancellationToken);
        if (submodules.Count == 0)
        {
            return;
        }

        var configurations = ReadGitModules(sourceDirectory);
        foreach (var submodule in submodules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedPath = submodule.Path.Replace('\\', '/').Trim('/');
            if (!configurations.TryGetValue(normalizedPath, out var url))
            {
                throw new InvalidDataException(localizationService.Format(
                    "Для Git submodule «{0}» не найден URL в .gitmodules.",
                    normalizedPath));
            }

            var submoduleRepository = ResolveGitHubRepository(repositoryFullName, url);
            var targetDirectory = ResolveSubmoduleDirectory(sourceDirectory, normalizedPath);
            Directory.CreateDirectory(targetDirectory);

            await using var archive = await gitHubService.DownloadRepositoryArchiveAsync(
                submoduleRepository,
                submodule.CommitSha,
                accessToken,
                cancellationToken);
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
            ExtractGitHubArchive(zip, targetDirectory);

            await PopulateGitHubSubmodulesAsync(
                submoduleRepository,
                submodule.CommitSha,
                targetDirectory,
                accessToken,
                depth + 1,
                cancellationToken);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadGitModules(string sourceDirectory)
    {
        var path = Path.Combine(sourceDirectory, ".gitmodules");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return result;
        }

        string? currentPath = null;
        string? currentUrl = null;
        foreach (var rawLine in File.ReadLines(path).Append("[end]"))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(currentPath) &&
                    !string.IsNullOrWhiteSpace(currentUrl))
                {
                    result[currentPath.Replace('\\', '/').Trim('/')] = currentUrl;
                }

                currentPath = null;
                currentUrl = null;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                currentPath = value;
            }
            else if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                currentUrl = value;
            }
        }

        return result;
    }

    private string ResolveGitHubRepository(string parentRepository, string url)
    {
        var value = url.Trim();
        if (value.StartsWith("../", StringComparison.Ordinal))
        {
            var owner = parentRepository.Split('/')[0];
            var repositoryName = value[3..].TrimEnd('/');
            if (repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repositoryName = repositoryName[..^4];
            }

            return $"{owner}/{repositoryName}";
        }

        const string httpsPrefix = "https://github.com/";
        const string sshPrefix = "git@github.com:";
        if (value.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[httpsPrefix.Length..];
        }
        else if (value.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[sshPrefix.Length..];
        }
        else
        {
            throw new InvalidDataException(localizationService.Format(
                "Git submodule использует неподдерживаемый URL: {0}",
                url));
        }

        value = value.Trim('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        if (value.Split('/').Length != 2)
        {
            throw new InvalidDataException(localizationService.Format(
                "Некорректный GitHub URL для submodule: {0}",
                url));
        }

        return value;
    }

    private static string ResolveSubmoduleDirectory(string root, string relativePath)
    {
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Git submodule path is outside the repository.");
        }

        return target;
    }

    private ProjectBuildProvider DetectProvider(
        KKProject project,
        string sourceDirectory)
    {
        if (project.BuildProvider != ProjectBuildProvider.Unknown)
        {
            return project.BuildProvider;
        }

        var detected = new List<ProjectBuildProvider>();
        if (Directory.EnumerateFiles(sourceDirectory, "*.sln*", SearchOption.TopDirectoryOnly).Any() ||
            Directory.EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories).Any())
        {
            detected.Add(ProjectBuildProvider.DotNet);
        }
        if (File.Exists(Path.Combine(sourceDirectory, "go.mod")))
        {
            detected.Add(ProjectBuildProvider.Go);
        }
        if (File.Exists(Path.Combine(sourceDirectory, "pyproject.toml")) ||
            File.Exists(Path.Combine(sourceDirectory, "requirements.txt")) ||
            Directory.EnumerateFiles(sourceDirectory, "*.py", SearchOption.TopDirectoryOnly).Any())
        {
            detected.Add(ProjectBuildProvider.Python);
        }
        if (File.Exists(Path.Combine(sourceDirectory, "CMakeLists.txt")) ||
            Directory.EnumerateFiles(sourceDirectory, "*.cpp", SearchOption.AllDirectories).Any())
        {
            detected.Add(ProjectBuildProvider.Cpp);
        }

        return detected.Count switch
        {
            1 => detected[0],
            0 => throw new InvalidOperationException(localizationService.Get(
                "Не удалось автоматически определить способ сборки проекта.")),
            _ => throw new InvalidOperationException(localizationService.Get(
                "Найдено несколько способов сборки. Выберите нужный в настройках проекта.")),
        };
    }

    private async Task BuildAsync(
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
                    var target = SelectDotNetProject(
                        sourceDirectory,
                        project.RemoteExecutableFileName);
                    var rid = MapDotNetRuntime(remoteArchitecture);
                    var arguments = new List<string>
                {
                    "publish",
                    target,
                    "-c",
                    string.IsNullOrWhiteSpace(configuration.Configuration)
                        ? "Release"
                        : configuration.Configuration.Trim(),
                    "-r",
                    rid,
                    "--self-contained",
                    "true",
                    "-o",
                    outputDirectory,
                };
                    arguments.AddRange(buildArguments);
                    await RunProcessAsync(
                        "dotnet",
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
                        ["GOOS"] = "linux",
                        ["GOARCH"] = goArch,
                        ["CGO_ENABLED"] = "0",
                    };
                    var goOutputPath = Path.Combine(
                        outputDirectory,
                        project.RemoteExecutableFileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(goOutputPath)!);
                    var arguments = new List<string> { "build" };
                    arguments.AddRange(buildArguments);
                    arguments.AddRange(
                        ["-o", goOutputPath, "."]);
                    await RunProcessAsync(
                        "go",
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
                    var toolchainReplacements = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["{source}"] = sourceDirectory,
                        ["{output}"] = outputDirectory,
                        ["{runtime}"] = runtime,
                        ["{architecture}"] = remoteArchitecture,
                    };
                    var configuredToolchain = ReplacePlaceholders(
                        configuration.ToolchainFile ?? string.Empty,
                        toolchainReplacements);
                    var toolchainFile = Path.GetFullPath(
                        Path.IsPathRooted(configuredToolchain)
                            ? configuredToolchain
                            : Path.Combine(sourceDirectory, configuredToolchain));
                    if (string.IsNullOrWhiteSpace(configuredToolchain) ||
                        !File.Exists(toolchainFile))
                    {
                        throw new FileNotFoundException(localizationService.Get(
                            "Укажите существующий CMake toolchain-файл для сборки C++ под Linux."));
                    }

                    var buildDirectory = Path.Combine(
                        Path.GetDirectoryName(outputDirectory)!,
                        "cmake-build");
                    var buildConfiguration = string.IsNullOrWhiteSpace(configuration.Configuration)
                        ? "Release"
                        : configuration.Configuration.Trim();
                    var configure = new List<string>
                {
                    "-S",
                    sourceDirectory,
                    "-B",
                    buildDirectory,
                    "-G",
                    string.IsNullOrWhiteSpace(configuration.CmakeGenerator)
                        ? "Ninja"
                        : configuration.CmakeGenerator.Trim(),
                    $"-DCMAKE_BUILD_TYPE={buildConfiguration}",
                    $"-DCMAKE_TOOLCHAIN_FILE={toolchainFile}",
                    $"-DCMAKE_RUNTIME_OUTPUT_DIRECTORY={outputDirectory}",
                    $"-DKKVAR_TARGET_ARCHITECTURE={remoteArchitecture}",
                };
                    configure.AddRange(configureArguments);
                    await RunProcessAsync(
                        "cmake",
                        configure,
                        sourceDirectory,
                        environment,
                        cancellationToken);

                    var build = new List<string>
                {
                    "--build",
                    buildDirectory,
                    "--config",
                    buildConfiguration,
                };
                    build.AddRange(buildArguments);
                    await RunProcessAsync(
                        "cmake",
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
                        throw new InvalidOperationException(localizationService.Get(
                            "Укажите команду пользовательской сборки."));
                    }

                    var runtime = MapLinuxRuntime(remoteArchitecture);
                    var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["{source}"] = sourceDirectory,
                        ["{output}"] = outputDirectory,
                        ["{runtime}"] = runtime,
                        ["{architecture}"] = remoteArchitecture,
                    };
                    var customEnvironment = new Dictionary<string, string?>(
                        environment,
                        StringComparer.Ordinal)
                    {
                        ["KKVAR_SOURCE_DIR"] = sourceDirectory,
                        ["KKVAR_OUTPUT_DIR"] = outputDirectory,
                        ["KKVAR_RUNTIME"] = runtime,
                        ["KKVAR_ARCHITECTURE"] = remoteArchitecture,
                    };
                    await RunProcessAsync(
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
        "x86_64" or "amd64" => "linux-x64",
        "aarch64" or "arm64" => "linux-arm64",
        _ => throw new NotSupportedException(localizationService.Format(
            "Архитектура удалённой машины «{0}» пока не поддерживается для .NET.",
            architecture)),
    };

    private string MapGoArchitecture(string architecture) => architecture switch
    {
        "x86_64" or "amd64" => "amd64",
        "aarch64" or "arm64" => "arm64",
        _ => throw new NotSupportedException(localizationService.Format(
            "Архитектура удалённой машины «{0}» пока не поддерживается для Go.",
            architecture)),
    };

    private string MapLinuxRuntime(string architecture) => architecture switch
    {
        "x86_64" or "amd64" => "linux-x64",
        "aarch64" or "arm64" => "linux-arm64",
        _ => throw new NotSupportedException(localizationService.Format(
            "Архитектура удалённой машины «{0}» пока не поддерживается.",
            architecture)),
    };

    private static string ResolveBuildSourceDirectory(
        string? configuredDirectory,
        string sourceDirectory)
    {
        var value = string.IsNullOrWhiteSpace(configuredDirectory)
            ? sourceDirectory
            : configuredDirectory.Replace(
                "{source}",
                sourceDirectory,
                StringComparison.Ordinal);
        var path = Path.GetFullPath(
            Path.IsPathRooted(value)
                ? value
                : Path.Combine(sourceDirectory, value));
        var sourceRoot = Path.GetFullPath(sourceDirectory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, sourceDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Build working directory must stay inside the source directory.");
        }
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Build working directory was not found: {path}");
        }

        return path;
    }

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

    private static string GetBuiltExecutableRelativePath(
        KKProject project,
        ProjectBuildProvider provider) =>
        provider == ProjectBuildProvider.DotNet &&
        project.RemoteExecutableFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? project.RemoteExecutableFileName[..^4]
            : project.RemoteExecutableFileName;

    private async Task RunProcessAsync(
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
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(localizationService.Format(
                    "Не удалось запустить {0}.",
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
                "Сборка завершилась с кодом {0}: {1}",
                process.ExitCode,
                LastUsefulLine(error, output)));
        }
    }

    private string GetMissingBuildToolMessage(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant() switch
        {
            "go" => localizationService.Get(
                "Go SDK не найден. Установите Go и перезапустите KK.Var. При запуске из Visual Studio перезапустите также Visual Studio."),
            "dotnet" => localizationService.Get(
                ".NET SDK не найден. Установите .NET SDK и перезапустите KK.Var."),
            "cmake" => localizationService.Get(
                "CMake не найден. Установите CMake и перезапустите KK.Var."),
            _ => localizationService.Format(
                "Команда сборки «{0}» не найдена. Установите необходимый инструмент и перезапустите KK.Var.",
                fileName),
        };

    private string SelectDotNetProject(
        string sourceDirectory,
        string executableFileName)
    {
        var projects = Directory
            .EnumerateFiles(sourceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Document = XDocument.Load(path),
            })
            .Where(item =>
            {
                var outputType = item.Document
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "OutputType")
                    ?.Value;
                return outputType is not null &&
                       (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
                        outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        var executableName = Path.GetFileNameWithoutExtension(executableFileName);
        var matching = projects.Where(item =>
        {
            var assemblyName = item.Document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                ?.Value ?? Path.GetFileNameWithoutExtension(item.Path);
            return assemblyName.Equals(executableName, StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        return matching.Length switch
        {
            1 => matching[0].Path,
            0 when projects.Length == 1 => projects[0].Path,
            0 => throw new InvalidOperationException(localizationService.Get(
                "Не удалось однозначно выбрать исполняемый .csproj. Имя выходной сборки должно совпадать с исполняемым файлом проекта.")),
            _ => throw new InvalidOperationException(localizationService.Get(
                "Найдено несколько исполняемых .csproj с одинаковым именем сборки.")),
        };
    }

    private string LastUsefulLine(params string[] values)
    {
        var lines = values
            .SelectMany(value => value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var useful = lines.LastOrDefault(line =>
            !line.StartsWith("See also", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("failed:", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("not recognized", StringComparison.OrdinalIgnoreCase)));

        return useful
            ?? lines.LastOrDefault(line =>
                !line.StartsWith("See also", StringComparison.OrdinalIgnoreCase))
            ?? localizationService.Get("неизвестная ошибка");
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IgnoredDirectories.Contains(Path.GetFileName(directory)))
            {
                continue;
            }
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)),
                cancellationToken);
        }
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private void ExtractGitHubArchive(ZipArchive zip, string destination)
    {
        var prefix = zip.Entries
            .Select(entry => entry.FullName.Split('/')[0])
            .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment))
            ?? throw new InvalidDataException(localizationService.Get(
                "Архив GitHub пуст."));

        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.StartsWith(prefix + "/", StringComparison.Ordinal)
                ? entry.FullName[(prefix.Length + 1)..]
                : entry.FullName;
            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(localizationService.Get(
                    "Архив GitHub содержит небезопасный путь."));
            }
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }
    }

    private static async Task CreateTarGzAsync(
        string sourceDirectory,
        string artifactPath,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            artifactPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await using var writer = new TarWriter(gzip, leaveOpen: false);
        foreach (var path in Directory.EnumerateFileSystemEntries(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/');
            writer.WriteEntry(path, relative);
        }
    }

    private static async Task<string> CalculateSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string FormatProvider(ProjectBuildProvider provider) => provider switch
    {
        ProjectBuildProvider.DotNet => ".NET",
        ProjectBuildProvider.Python => "Python",
        ProjectBuildProvider.Go => "Go",
        ProjectBuildProvider.Cpp => "C++",
        ProjectBuildProvider.Custom => "свой сценарий",
        _ => provider.ToString(),
    };
}
