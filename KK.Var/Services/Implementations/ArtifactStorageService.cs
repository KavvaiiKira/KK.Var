using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class ArtifactStorageService(
    IUserSettingsService userSettingsService,
    IKKProjectVersionRepository versionRepository,
    IDeploymentOperationQueue operationQueue,
    ILocalizationService localizationService) : IArtifactStorageService
{
    private readonly SemaphoreSlim _migrationLock = new SemaphoreSlim(1, 1);
    private int _migrationRunning;

    public bool IsMigrationRunning => Volatile.Read(ref _migrationRunning) != 0;

    public async Task<string> GetEffectiveRootAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await userSettingsService.LoadAsync(cancellationToken);
        return GetEffectiveRoot(settings);
    }

    public async Task<ArtifactStorageSummary> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var root = await GetEffectiveRootAsync(cancellationToken);
        var usedBytes = await Task.Run(
            () => CalculateDirectorySize(root, cancellationToken),
            cancellationToken);

        return new ArtifactStorageSummary(root, usedBytes);
    }

    public async Task<ArtifactFileState> GetFileStateAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = await ResolvePathAsync(relativePath, cancellationToken);
        if (!File.Exists(path))
        {
            return new ArtifactFileState(path, false, 0);
        }

        EnsureNotReparsePoint(path);
        return new ArtifactFileState(path, true, new FileInfo(path).Length);
    }

    public async Task<string> ResolvePathAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = await GetEffectiveRootAsync(cancellationToken);
        return ResolvePath(root, relativePath);
    }

    public async Task DeleteProjectArtifactsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        var root = await GetEffectiveRootAsync(cancellationToken);
        var projectDirectory = ResolvePath(root, projectId.ToString("N"));

        if (!Directory.Exists(projectDirectory))
        {
            return;
        }

        EnsureTreeHasNoReparsePoints(new DirectoryInfo(projectDirectory));
        Directory.Delete(projectDirectory, recursive: true);
    }

    public async Task DeleteArtifactAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = await ResolvePathAsync(relativePath, cancellationToken);
        if (!File.Exists(path))
        {
            return;
        }

        EnsureNotReparsePoint(path);
        File.Delete(path);

        var root = await GetEffectiveRootAsync(cancellationToken);
        var directory = Directory.GetParent(path);
        if (directory is not null &&
            !string.Equals(directory.FullName, root, StringComparison.OrdinalIgnoreCase) &&
            !directory.EnumerateFileSystemInfos().Any())
        {
            directory.Delete();
        }
    }

    public async Task<ArtifactStorageMigrationPlan> PlanMigrationAsync(
        string? targetRoot,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = await GetEffectiveRootAsync(cancellationToken);
        var validatedTargetRoot = ValidateRoot(targetRoot);

        return await BuildPlanAsync(
            sourceRoot,
            validatedTargetRoot,
            verifyHashes: true,
            cancellationToken);
    }

    public async Task<ArtifactStorageMigrationResult> MigrateAsync(
        string? targetRoot,
        CancellationToken cancellationToken = default)
    {
        if (operationQueue.HasActiveOperations())
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя менять каталог архивов во время активной операции."));
        }

        await _migrationLock.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _migrationRunning, 1);

        try
        {
            if (operationQueue.HasActiveOperations())
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Нельзя менять каталог архивов во время активной операции."));
            }

            var settings = await userSettingsService.LoadAsync(cancellationToken);
            var sourceRoot = GetEffectiveRoot(settings);
            var validatedTargetRoot = ValidateRoot(targetRoot);
            var plan = await BuildPlanAsync(
                sourceRoot,
                validatedTargetRoot,
                verifyHashes: true,
                cancellationToken);

            if (string.Equals(
                    plan.SourceRoot,
                    plan.TargetRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ArtifactStorageMigrationResult(plan, null);
            }

            Directory.CreateDirectory(plan.TargetRoot);
            EnsureExistingPathHasNoReparsePoints(plan.TargetRoot);

            var versions = await versionRepository.GetAllAsync(cancellationToken);
            EnsureFreeSpace(plan.TargetRoot, plan.TotalBytes, versions);

            var createdFiles = new List<string>();

            try
            {
                foreach (var version in versions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourcePath = ResolvePath(plan.SourceRoot, version.ArtifactRelativePath);
                    var targetPath = ResolvePath(plan.TargetRoot, version.ArtifactRelativePath);

                    if (File.Exists(targetPath))
                    {
                        await VerifyFileAsync(targetPath, version, cancellationToken);
                        continue;
                    }

                    var targetDirectory = Path.GetDirectoryName(targetPath) ??
                        throw new InvalidOperationException("Artifact target directory is invalid.");
                    Directory.CreateDirectory(targetDirectory);
                    EnsureExistingPathHasNoReparsePoints(targetDirectory);

                    var temporaryPath = targetPath + $".{Guid.NewGuid():N}.tmp";

                    try
                    {
                        await CopyFileAsync(sourcePath, temporaryPath, cancellationToken);
                        await VerifyFileAsync(temporaryPath, version, cancellationToken);
                        File.Move(temporaryPath, targetPath);
                        createdFiles.Add(targetPath);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                }

                settings.ArtifactsDirectoryPath = IsDefaultRoot(plan.TargetRoot) ?
                    null :
                    plan.TargetRoot;
                await userSettingsService.SaveAsync(settings, cancellationToken);
            }
            catch
            {
                foreach (var createdFile in createdFiles)
                {
                    if (File.Exists(createdFile))
                    {
                        File.Delete(createdFile);
                    }
                }

                throw;
            }

            var warning = DeleteSourceFiles(plan.SourceRoot, versions);
            return new ArtifactStorageMigrationResult(plan, warning);
        }
        finally
        {
            Interlocked.Exchange(ref _migrationRunning, 0);
            _migrationLock.Release();
        }
    }

    private async Task<ArtifactStorageMigrationPlan> BuildPlanAsync(
        string sourceRoot,
        string targetRoot,
        bool verifyHashes,
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new ArtifactStorageMigrationPlan(sourceRoot, targetRoot, 0, 0, 0);
        }

        if (IsSameOrChild(sourceRoot, targetRoot) || IsSameOrChild(targetRoot, sourceRoot))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Исходный и новый каталоги архивов не могут быть вложены друг в друга."));
        }

        if (File.Exists(targetRoot))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Выбранный путь указывает на файл."));
        }

        Directory.CreateDirectory(targetRoot);
        EnsureExistingPathHasNoReparsePoints(targetRoot);

        var versions = await versionRepository.GetAllAsync(cancellationToken);
        var totalBytes = 0L;
        var existingVerifiedFiles = 0;

        foreach (var version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            totalBytes = checked(totalBytes + version.ArtifactSize);

            var targetPath = ResolvePath(targetRoot, version.ArtifactRelativePath);
            if (File.Exists(targetPath))
            {
                await VerifyFileAsync(targetPath, version, cancellationToken);
                existingVerifiedFiles++;
                continue;
            }

            var sourcePath = ResolvePath(sourceRoot, version.ArtifactRelativePath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(localizationService.Format(
                    "Не найден архив версии «{0}» в текущем каталоге.",
                    version.Tag), sourcePath);
            }

            if (verifyHashes)
            {
                await VerifyFileAsync(sourcePath, version, cancellationToken);
            }
        }

        EnsureFreeSpace(targetRoot, totalBytes, versions);

        return new ArtifactStorageMigrationPlan(
            sourceRoot,
            targetRoot,
            versions.Count,
            totalBytes,
            existingVerifiedFiles);
    }

    private string GetEffectiveRoot(UserSettings settings) =>
        string.IsNullOrWhiteSpace(settings.ArtifactsDirectoryPath) ?
            Path.GetFullPath(DatabasePaths.ArtifactsDirectory) :
            ValidateRoot(settings.ArtifactsDirectoryPath);

    private string ValidateRoot(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path) ?
            DatabasePaths.ArtifactsDirectory :
            path.Trim();

        if (!Path.IsPathFullyQualified(candidate) || candidate.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new ArgumentException(localizationService.Get(
                "Каталог архивов должен быть абсолютным локальным путём."));
        }

        var fullPath = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathRoot = Path.GetPathRoot(fullPath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(pathRoot) ||
            string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(localizationService.Get(
                "Каталог архивов не может быть корнем диска."));
        }

        if (File.Exists(fullPath))
        {
            throw new ArgumentException(localizationService.Get(
                "Каталог архивов не может указывать на файл."));
        }

        foreach (var restrictedPath in GetRestrictedPaths())
        {
            if (IsSameOrChild(fullPath, restrictedPath))
            {
                throw new ArgumentException(localizationService.Get(
                    "Каталог архивов не может находиться в системной директории."));
            }
        }

        EnsureExistingPathHasNoReparsePoints(fullPath);
        return fullPath;
    }

    private string ResolvePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Путь к архиву версии должен быть относительным."));
        }

        var segments = relativePath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Путь к архиву версии содержит недопустимый сегмент."));
        }

        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Путь к архиву версии выходит за пределы каталога архивов."));
        }

        EnsureExistingPathHasNoReparsePoints(Path.GetDirectoryName(path) ?? normalizedRoot);
        return path;
    }

    private async Task VerifyFileAsync(
        string path,
        KKProjectVersion version,
        CancellationToken cancellationToken)
    {
        EnsureNotReparsePoint(path);

        var file = new FileInfo(path);
        if (file.Length != version.ArtifactSize)
        {
            throw new InvalidDataException(localizationService.Format(
                "Размер архива версии «{0}» не совпадает с сохранённым.",
                version.Tag));
        }

        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(
            stream,
            cancellationToken));

        if (!string.Equals(hash, version.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(localizationService.Format(
                "Контрольная сумма архива версии «{0}» не совпадает.",
                version.Tag));
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private void EnsureFreeSpace(
        string targetRoot,
        long totalBytes,
        IReadOnlyList<KKProjectVersion> versions)
    {
        var existingBytes = versions.Sum(version =>
        {
            var targetPath = ResolvePath(targetRoot, version.ArtifactRelativePath);
            return File.Exists(targetPath) ? version.ArtifactSize : 0;
        });
        var requiredBytes = totalBytes - existingBytes;
        var driveRoot = Path.GetPathRoot(targetRoot) ??
            throw new InvalidOperationException(localizationService.Get(
                "Не удалось определить диск выбранного каталога."));
        var drive = new DriveInfo(driveRoot);

        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException(localizationService.Get(
                "В выбранном каталоге недостаточно свободного места."));
        }
    }

    private string? DeleteSourceFiles(
        string sourceRoot,
        IReadOnlyList<KKProjectVersion> versions)
    {
        var failures = new List<string>();

        foreach (var version in versions)
        {
            try
            {
                var sourcePath = ResolvePath(sourceRoot, version.ArtifactRelativePath);
                if (File.Exists(sourcePath))
                {
                    EnsureNotReparsePoint(sourcePath);
                    File.Delete(sourcePath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException or
                NotSupportedException or
                System.Security.SecurityException)
            {
                failures.Add(version.ArtifactRelativePath);
            }
        }

        return failures.Count == 0 ?
            null :
            localizationService.Format(
                "Новый каталог уже используется, но не удалось удалить исходных файлов: {0}.",
                failures.Count);
    }

    private long CalculateDirectorySize(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var directory = new DirectoryInfo(root);
        EnsureTreeHasNoReparsePoints(directory);
        var total = 0L;

        foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            total = checked(total + file.Length);
        }

        return total;
    }

    private void EnsureExistingPathHasNoReparsePoints(string path)
    {
        var directory = new DirectoryInfo(path);

        while (directory is not null)
        {
            if (directory.Exists &&
                (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Путь к архивам не может проходить через reparse point."));
            }

            directory = directory.Parent;
        }
    }

    private void EnsureTreeHasNoReparsePoints(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Каталог архивов не может содержать reparse point."));
        }

        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Каталог архивов не может содержать reparse point."));
            }

            if (entry is DirectoryInfo childDirectory)
            {
                EnsureTreeHasNoReparsePoints(childDirectory);
            }
        }
    }

    private void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Файл архива не может быть reparse point."));
        }
    }

    private static IEnumerable<string> GetRestrictedPaths()
    {
        var paths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        };

        return paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath);
    }

    private static bool IsSameOrChild(string path, string parent)
    {
        var normalizedParent = parent.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return string.Equals(path, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultRoot(string path) =>
        string.Equals(
            path,
            Path.GetFullPath(DatabasePaths.ArtifactsDirectory),
            StringComparison.OrdinalIgnoreCase);
}
