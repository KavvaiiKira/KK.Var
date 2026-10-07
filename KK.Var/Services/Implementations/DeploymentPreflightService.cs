using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class DeploymentPreflightService(
    IKKProjectRepository projectRepository,
    IKKProjectVersionRepository versionRepository,
    IKKProjectEnvironmentService environmentService,
    IGitHubService gitHubService,
    IGitHubAuthenticationService gitHubAuthenticationService,
    IUserSettingsService userSettingsService,
    IArtifactStorageService artifactStorageService,
    IDeploymentOperationQueue operationQueue,
    IRemoteConnectionService remoteConnectionService,
    ILocalizationService localizationService) : IDeploymentPreflightService
{
    private static readonly Regex VersionTagPattern = new Regex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> IgnoredSourceDirectories = new HashSet<string>(
        [".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", ".venv"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<DeploymentPreflightResult> CheckAsync(
        DeploymentRequest request,
        bool currentOperationOwnsQueue = false,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DeploymentPreflightCheck>();
        var project = await projectRepository.GetByIdAsync(
            request.ProjectId,
            cancellationToken);

        if (project is null)
        {
            checks.Add(Error("project", "Проект не найден."));
            return new DeploymentPreflightResult(checks);
        }

        checks.Add(!currentOperationOwnsQueue &&
                   operationQueue.HasActiveOperation(project.Id) ?
            Error("queue", "Для этого проекта уже есть активная операция.") :
            Success("queue", "Для проекта нет конфликтующей операции."));

        var tag = request.VersionTag?.Trim() ?? string.Empty;
        if (!VersionTagPattern.IsMatch(tag))
        {
            checks.Add(Error(
                "version",
                "Тег версии может содержать только латинские буквы, цифры, точку, дефис и подчёркивание."));
        }
        else if (await versionRepository.TagExistsAsync(project.Id, tag, cancellationToken))
        {
            checks.Add(new DeploymentPreflightCheck(
                "version",
                PreflightStatus.Error,
                localizationService.Format("Версия «{0}» уже существует.", tag)));
        }
        else
        {
            checks.Add(Success("version", "Тег новой версии доступен."));
        }

        var settings = await userSettingsService.LoadAsync(cancellationToken);
        var sourceBytes = await CheckSourceAsync(project, checks, cancellationToken);
        await CheckBuildConfigurationAsync(project, checks, cancellationToken);

        try
        {
            _ = await environmentService.GenerateAsync(project.Id, cancellationToken);
            checks.Add(Success("environment", "Файл переменных можно сформировать."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Error("environment", "Файл переменных некорректен.",
                exception.Message));
        }

        const long minimumFreeBytes = 64L * 1024 * 1024;
        if (sourceBytes > minimumFreeBytes)
        {
            checks.Add(Warning(
                "space-estimate",
                "Итоговый размер сборки неизвестен; проверен минимальный запас места."));
        }

        CheckRemotePath(project, checks);
        await CheckLocalStorageAsync(checks, minimumFreeBytes, cancellationToken);

        if (checks.Any(check =>
                check.Key == "remote-path" && check.Status == PreflightStatus.Error))
        {
            return new DeploymentPreflightResult(checks);
        }

        var remoteChecks = await remoteConnectionService.CheckDeploymentAsync(
            settings.RemoteMachine,
            project.RemoteDeploymentDirectory,
            minimumFreeBytes,
            project.BuildProvider == ProjectBuildProvider.Python ||
                project.RemoteExecutableFileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase),
            cancellationToken);
        checks.AddRange(remoteChecks);

        return new DeploymentPreflightResult(checks);
    }

    private async Task<long> CheckSourceAsync(
        KKProject project,
        List<DeploymentPreflightCheck> checks,
        CancellationToken cancellationToken)
    {
        if (project.SourceType == ProjectSourceType.LocalDirectory)
        {
            try
            {
                var path = project.LocalDirectoryPath;
                if (string.IsNullOrWhiteSpace(path) ||
                    !Path.IsPathFullyQualified(path) ||
                    !Directory.Exists(path))
                {
                    throw new DirectoryNotFoundException(path);
                }

                var bytes = await Task.Run(
                    () => EstimateSourceBytes(path, cancellationToken),
                    cancellationToken);

                checks.Add(Success("source", "Локальный исходный каталог доступен."));
                return bytes;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(Error("source", "Локальный исходный каталог недоступен.",
                    exception.Message));
                return 0;
            }
        }

        if (project.SourceType == ProjectSourceType.GitHubRepository)
        {
            try
            {
                var token = await gitHubAuthenticationService.GetTokenAsync(cancellationToken) ??
                    throw new InvalidOperationException(localizationService.Get(
                        "Подключите GitHub в настройках перед сборкой проекта."));
                var commitSha = await gitHubService.GetDefaultBranchCommitShaAsync(
                    project.GitHubRepositoryFullName ?? string.Empty,
                    token.AccessToken,
                    cancellationToken);
                checks.Add(Success("source", "GitHub-репозиторий доступен.", commitSha));
                checks.Add(Warning("source-size", "Размер GitHub-архива станет известен после загрузки."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(Error("source", "GitHub-репозиторий недоступен.",
                    exception.Message));
            }

            return 0;
        }

        checks.Add(Error("source", "Тип источника проекта недопустим."));
        return 0;
    }

    private async Task CheckBuildConfigurationAsync(
        KKProject project,
        List<DeploymentPreflightCheck> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<ProjectBuildConfiguration>(
                project.BuildConfigurationJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                throw new JsonException("Build configuration is empty.");

            if (configuration.BuildArguments is null ||
                configuration.ConfigureArguments is null ||
                configuration.Environment is null ||
                configuration.BuildArguments.Any(argument => argument is null) ||
                configuration.ConfigureArguments.Any(argument => argument is null))
            {
                throw new InvalidDataException("Build arguments or environment are invalid.");
            }

            var provider = project.BuildProvider;
            if (provider == ProjectBuildProvider.Unknown &&
                project.SourceType == ProjectSourceType.LocalDirectory &&
                !string.IsNullOrWhiteSpace(project.LocalDirectoryPath) &&
                Directory.Exists(project.LocalDirectoryPath))
            {
                provider = DetectProvider(project.LocalDirectoryPath);
            }

            if (provider == ProjectBuildProvider.Unknown)
            {
                checks.Add(Warning("build", "Способ сборки будет определён после загрузки исходников."));
                return;
            }

            var sourceRoot = project.SourceType == ProjectSourceType.LocalDirectory ?
                project.LocalDirectoryPath :
                null;
            if (sourceRoot is null)
            {
                checks.Add(Warning(
                    "build-source",
                    "Входные файлы сборки будут проверены после загрузки GitHub-архива."));
            }

            if (sourceRoot is not null && !Directory.Exists(sourceRoot))
            {
                return;
            }

            if (sourceRoot is not null &&
                !string.IsNullOrWhiteSpace(configuration.WorkingDirectory))
            {
                var workPath = Path.GetFullPath(Path.Combine(
                    sourceRoot,
                    configuration.WorkingDirectory.Replace(
                        "{source}",
                        sourceRoot,
                        StringComparison.Ordinal)));
                var sourcePrefix = Path.GetFullPath(sourceRoot).TrimEnd(
                    Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!string.Equals(workPath, Path.GetFullPath(sourceRoot),
                        StringComparison.OrdinalIgnoreCase) &&
                    !workPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Build working directory leaves the source directory.");
                }

                if (!Directory.Exists(workPath))
                {
                    throw new DirectoryNotFoundException(workPath);
                }
            }

            if (sourceRoot is not null)
            {
                var buildRoot = string.IsNullOrWhiteSpace(configuration.WorkingDirectory) ?
                    sourceRoot :
                    Path.GetFullPath(Path.Combine(sourceRoot,
                        configuration.WorkingDirectory.Replace(
                            "{source}", sourceRoot, StringComparison.Ordinal)));
                var hasInputs = provider switch
                {
                    ProjectBuildProvider.DotNet =>
                        Directory.EnumerateFiles(buildRoot, "*.csproj",
                            SearchOption.AllDirectories).Any(),
                    ProjectBuildProvider.Go =>
                        File.Exists(Path.Combine(buildRoot, "go.mod")),
                    ProjectBuildProvider.Python =>
                        File.Exists(Path.Combine(sourceRoot,
                            project.RemoteExecutableFileName.Replace('/',
                                Path.DirectorySeparatorChar))),
                    ProjectBuildProvider.Cpp =>
                        File.Exists(Path.Combine(buildRoot, "CMakeLists.txt")),
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
                    "{source}", sourceRoot, StringComparison.Ordinal);
                if (toolchain.Contains('{'))
                {
                    checks.Add(Warning(
                        "toolchain",
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
                ProjectBuildProvider.DotNet => "dotnet",
                ProjectBuildProvider.Go => "go",
                ProjectBuildProvider.Cpp => "cmake",
                ProjectBuildProvider.Custom => configuration.Command,
                _ => null,
            };

            if (tool?.Contains('{') == true)
            {
                checks.Add(Warning(
                    "build-tool",
                    "Команда сборки будет проверена после подстановки параметров."));
            }

            if (tool is not null &&
                !tool.Contains('{') &&
                !ToolExists(tool, sourceRoot))
            {
                throw new FileNotFoundException(localizationService.Format(
                    "Инструмент сборки «{0}» не найден.", tool));
            }

            if (provider == ProjectBuildProvider.DotNet &&
                !await HasDotNetSdkAsync(cancellationToken))
            {
                throw new InvalidOperationException(localizationService.Get(
                    ".NET SDK не найден. Установите .NET SDK и перезапустите KK.Var."));
            }

            if (provider == ProjectBuildProvider.Cpp &&
                (string.IsNullOrWhiteSpace(configuration.CmakeGenerator) ||
                 configuration.CmakeGenerator.Equals("Ninja", StringComparison.OrdinalIgnoreCase)) &&
                !ToolExists("ninja"))
            {
                throw new FileNotFoundException(localizationService.Get(
                    "Ninja не найден."));
            }

            checks.Add(Success("build", "Build-конфигурация и локальный инструмент проверены."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Error("build", "Build-конфигурация или инструмент недоступны.",
                exception.Message));
        }
    }

    private async Task CheckLocalStorageAsync(
        List<DeploymentPreflightCheck> checks,
        long minimumFreeBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = await artifactStorageService.GetEffectiveRootAsync(cancellationToken);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".kk-var-preflight-{Guid.NewGuid():N}.tmp");
            try
            {
                await using var stream = new FileStream(
                    probe,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
            }
            finally
            {
                if (File.Exists(probe))
                {
                    File.Delete(probe);
                }
            }

            var drive = new DriveInfo(Path.GetPathRoot(root) ?? root);
            checks.Add(new DeploymentPreflightCheck(
                "local-space",
                drive.AvailableFreeSpace >= minimumFreeBytes ?
                    PreflightStatus.Success :
                    PreflightStatus.Error,
                localizationService.Get(drive.AvailableFreeSpace >= minimumFreeBytes ?
                    "Локальный архив можно создать; свободное место проверено." :
                    "Недостаточно места для локального архива."),
                localizationService.Format("Доступно байт: {0}; требуется минимум: {1}.",
                    drive.AvailableFreeSpace,
                    minimumFreeBytes)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            checks.Add(Error("local-space", "Не удалось подготовить локальный архив.",
                exception.Message));
        }
    }

    private void CheckRemotePath(
        KKProject project,
        List<DeploymentPreflightCheck> checks)
    {
        var path = project.RemoteDeploymentDirectory;
        var segments = path?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        var restricted = new[]
        {
            "/bin/", "/boot/", "/dev/", "/etc/", "/lib/", "/lib64/",
            "/proc/", "/run/", "/sbin/", "/sys/", "/usr/bin/",
            "/usr/lib/", "/usr/lib64/", "/usr/sbin/", "/usr/share/",
        };
        var valid = !string.IsNullOrWhiteSpace(path) &&
            path.StartsWith("/", StringComparison.Ordinal) &&
            path.IndexOfAny(['\0', '\r', '\n']) < 0 &&
            segments.Length >= 2 &&
            !(segments[0].Equals("home", StringComparison.OrdinalIgnoreCase) &&
              segments.Length < 3) &&
            !segments.Any(segment => segment is "." or "..") &&
            !restricted.Any(prefix =>
                (path.TrimEnd('/') + "/").StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)) &&
            IsSafeRelativeFilePath(project.ProjectEnvironmentFilePath) &&
            IsSafeRelativeFilePath(project.RemoteExecutableFileName);

        checks.Add(valid ?
            Success("remote-path", "Удалённый путь безопасен для Deploy.") :
            Error("remote-path", "Удалённый путь Deploy небезопасен."));
    }

    private static bool IsSafeRelativeFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathRooted(value) ||
            value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            return false;
        }

        return value.Replace('\\', '/').Split('/')
            .All(segment => !string.IsNullOrWhiteSpace(segment) &&
                segment is not ("." or ".."));
    }

    private static ProjectBuildProvider DetectProvider(string sourceDirectory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var found = new List<ProjectBuildProvider>();
        if (Directory.EnumerateFiles(sourceDirectory, "*.csproj", options).Any())
        {
            found.Add(ProjectBuildProvider.DotNet);
        }

        if (File.Exists(Path.Combine(sourceDirectory, "go.mod")))
        {
            found.Add(ProjectBuildProvider.Go);
        }

        if (File.Exists(Path.Combine(sourceDirectory, "pyproject.toml")) ||
            File.Exists(Path.Combine(sourceDirectory, "requirements.txt")) ||
            Directory.EnumerateFiles(sourceDirectory, "*.py").Any())
        {
            found.Add(ProjectBuildProvider.Python);
        }

        if (File.Exists(Path.Combine(sourceDirectory, "CMakeLists.txt")) ||
            Directory.EnumerateFiles(sourceDirectory, "*.cpp", options).Any())
        {
            found.Add(ProjectBuildProvider.Cpp);
        }

        if (found.Count != 1)
        {
            throw new InvalidDataException("Build provider cannot be detected unambiguously.");
        }

        return found[0];
    }

    private static long EstimateSourceBytes(
        string root,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var bytes = 0L;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    bytes = checked(bytes + info.Length);
                }
            }

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                var info = new DirectoryInfo(child);
                if (!IgnoredSourceDirectories.Contains(info.Name) &&
                    (info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }

        return bytes;
    }

    private static bool ToolExists(string tool, string? sourceRoot = null)
    {
        if (Path.IsPathFullyQualified(tool))
        {
            return File.Exists(tool);
        }

        if (tool.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return File.Exists(Path.GetFullPath(Path.Combine(
                sourceRoot ?? Directory.GetCurrentDirectory(), tool)));
        }

        var extensions = OperatingSystem.IsWindows() ?
            new[] { ".exe", ".cmd", ".bat", string.Empty } :
            new[] { string.Empty };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
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

    private static async Task<bool> HasDotNetSdkAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--list-sdks");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The dotnet process could not be started.");
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

    private DeploymentPreflightCheck Success(string key, string message, string? detail = null) =>
        new DeploymentPreflightCheck(key, PreflightStatus.Success,
            localizationService.Get(message), detail);

    private DeploymentPreflightCheck Warning(string key, string message, string? detail = null) =>
        new DeploymentPreflightCheck(key, PreflightStatus.Warning,
            localizationService.Get(message), detail);

    private DeploymentPreflightCheck Error(string key, string message, string? detail = null) =>
        new DeploymentPreflightCheck(key, PreflightStatus.Error,
            localizationService.Get(message), detail);

}
