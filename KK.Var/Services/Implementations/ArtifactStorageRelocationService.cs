using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations
{
    public sealed class ArtifactStorageRelocationService(
        IUserSettingsService userSettingsService,
        IKKProjectVersionRepository versionRepository,
        IDeploymentOperationQueue operationQueue,
        IArtifactStorageActivity storageActivity,
        IArtifactPathPolicy pathPolicy,
        ILocalizationService localizationService) : IArtifactStorageRelocationService
    {
        public async Task<ArtifactStorageMigrationPlan> PlanMigrationAsync(
            string? targetRoot,
            CancellationToken cancellationToken = default)
        {
            var sourceRoot = pathPolicy.GetEffectiveRoot(await userSettingsService.LoadAsync(cancellationToken));
            var validatedTargetRoot = pathPolicy.ValidateRoot(targetRoot);

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

            await using var lease = await storageActivity.AcquireMigrationLeaseAsync(cancellationToken);

            if (operationQueue.HasActiveOperations())
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Нельзя менять каталог архивов во время активной операции."));
            }

            var settings = await userSettingsService.LoadAsync(cancellationToken);
            var sourceRoot = pathPolicy.GetEffectiveRoot(settings);
            var validatedTargetRoot = pathPolicy.ValidateRoot(targetRoot);
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
            pathPolicy.EnsureExistingPathHasNoReparsePoints(plan.TargetRoot);

            var versions = await versionRepository.GetAllAsync(cancellationToken);
            EnsureFreeSpace(plan.TargetRoot, plan.TotalBytes, versions);

            var createdFiles = new List<string>();

            try
            {
                foreach (var version in versions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourcePath = pathPolicy.ResolvePath(plan.SourceRoot, version.ArtifactRelativePath);
                    var targetPath = pathPolicy.ResolvePath(plan.TargetRoot, version.ArtifactRelativePath);

                    if (File.Exists(targetPath))
                    {
                        await VerifyFileAsync(targetPath, version, cancellationToken);
                        continue;
                    }

                    var targetDirectory = Path.GetDirectoryName(targetPath) ??
                        throw new InvalidOperationException(localizationService.Get("Целевой каталог архивов недопустим."));
                    Directory.CreateDirectory(targetDirectory);
                    pathPolicy.EnsureExistingPathHasNoReparsePoints(targetDirectory);

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

                settings.ArtifactsDirectoryPath = pathPolicy.IsDefaultRoot(plan.TargetRoot) ?
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

            if (pathPolicy.IsSameOrChild(sourceRoot, targetRoot) || pathPolicy.IsSameOrChild(
                targetRoot,
                sourceRoot))
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
            pathPolicy.EnsureExistingPathHasNoReparsePoints(targetRoot);

            var versions = await versionRepository.GetAllAsync(cancellationToken);
            var totalBytes = 0L;
            var existingVerifiedFiles = 0;

            foreach (var version in versions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                totalBytes = checked(totalBytes + version.ArtifactSize);

                var targetPath = pathPolicy.ResolvePath(targetRoot, version.ArtifactRelativePath);
                if (File.Exists(targetPath))
                {
                    await VerifyFileAsync(targetPath, version, cancellationToken);
                    existingVerifiedFiles++;
                    continue;
                }

                var sourcePath = pathPolicy.ResolvePath(sourceRoot, version.ArtifactRelativePath);
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

        private async Task VerifyFileAsync(
            string path,
            KKProjectVersion version,
            CancellationToken cancellationToken)
        {
            pathPolicy.EnsureNotReparsePoint(path);

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
                var targetPath = pathPolicy.ResolvePath(targetRoot, version.ArtifactRelativePath);
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
                    var sourcePath = pathPolicy.ResolvePath(sourceRoot, version.ArtifactRelativePath);
                    if (File.Exists(sourcePath))
                    {
                        pathPolicy.EnsureNotReparsePoint(sourcePath);
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
    }
}
