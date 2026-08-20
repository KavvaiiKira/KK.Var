using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    IGitHubTokenStore gitHubTokenStore,
    ILocalizationService localizationService) : IProjectArtifactService
{
    private static readonly Regex TagPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> IgnoredDirectories = new(
        [".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", ".venv"],
        StringComparer.OrdinalIgnoreCase);

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
            await AcquireSourceAsync(project, sourceDirectory, cancellationToken);

            var provider = DetectProvider(project, sourceDirectory);
            progress?.Report(new DeploymentProgress(
                15,
                localizationService.Format(
                    "Сборка: {0}",
                    FormatProvider(provider))));
            await BuildAsync(
                provider,
                project,
                sourceDirectory,
                outputDirectory,
                remoteArchitecture,
                cancellationToken);

            await environmentService.WriteFileAsync(
                project.Id,
                outputDirectory,
                cancellationToken);

            var executablePath = Path.GetFullPath(Path.Combine(
                outputDirectory,
                project.RemoteExecutableFileName.Replace('/', Path.DirectorySeparatorChar)));
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
                null,
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

    private async Task AcquireSourceAsync(
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
            return;
        }

        var repository = project.GitHubRepositoryFullName
            ?? throw new InvalidOperationException(localizationService.Get(
                "Не указан репозиторий GitHub."));
        var token = await gitHubTokenStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Подключите GitHub в настройках перед сборкой проекта."));
        }

        await using var archive = await gitHubService.DownloadRepositoryArchiveAsync(
            repository,
            token,
            cancellationToken);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        ExtractGitHubArchive(zip, destination);
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
        string sourceDirectory,
        string outputDirectory,
        string remoteArchitecture,
        CancellationToken cancellationToken)
    {
        switch (provider)
        {
            case ProjectBuildProvider.DotNet:
            {
                var target = SelectDotNetProject(
                    sourceDirectory,
                    project.RemoteExecutableFileName);
                var rid = MapDotNetRuntime(remoteArchitecture);
                await RunProcessAsync(
                    "dotnet",
                    ["publish", target, "-c", "Release", "-r", rid, "--self-contained", "true", "-o", outputDirectory],
                    sourceDirectory,
                    null,
                    cancellationToken);
                break;
            }
            case ProjectBuildProvider.Go:
            {
                var goArch = MapGoArchitecture(remoteArchitecture);
                await RunProcessAsync(
                    "go",
                    ["build", "-o", Path.Combine(outputDirectory, project.RemoteExecutableFileName), "."],
                    sourceDirectory,
                    new Dictionary<string, string?>
                    {
                        ["GOOS"] = "linux",
                        ["GOARCH"] = goArch,
                        ["CGO_ENABLED"] = "0",
                    },
                    cancellationToken);
                break;
            }
            case ProjectBuildProvider.Python:
                CopyDirectory(sourceDirectory, outputDirectory, cancellationToken);
                break;
            case ProjectBuildProvider.Cpp:
                throw new NotSupportedException(localizationService.Get(
                    "Для C++ требуется настроенный Linux cross-toolchain. Он будет добавлен как отдельная конфигурация сборки."));
            case ProjectBuildProvider.Custom:
                throw new NotSupportedException(localizationService.Get(
                    "Свой сценарий сборки ещё не настроен для этого проекта."));
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
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(localizationService.Format(
                "Не удалось запустить {0}.",
                fileName));
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
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

    private string LastUsefulLine(params string[] values) => values
        .SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        .LastOrDefault() ?? localizationService.Get("неизвестная ошибка");

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
