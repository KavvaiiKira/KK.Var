using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class ArtifactStorageService(
        IUserSettingsService userSettingsService,
        IArtifactStorageActivity storageActivity,
        IArtifactPathPolicy pathPolicy) : IArtifactStorageService
    {
        public async Task<string> GetEffectiveRootAsync(
            CancellationToken cancellationToken = default)
        {
            var settings = await userSettingsService.LoadAsync(cancellationToken);
            return pathPolicy.GetEffectiveRoot(settings);
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
            var attributes = pathPolicy.GetExistingAttributes(path);
            if (!attributes.HasValue)
            {
                return new ArtifactFileState(path, false, 0);
            }

            if ((attributes.Value & FileAttributes.Directory) != 0)
            {
                throw new InvalidOperationException(Texts.VersionArchivePathInvalid);
            }

            pathPolicy.EnsureNotReparsePoint(path);
            return new ArtifactFileState(path, true, new FileInfo(path).Length);
        }

        public async Task<string> ResolvePathAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            var root = await GetEffectiveRootAsync(cancellationToken);
            return pathPolicy.ResolvePath(root, relativePath);
        }

        public async Task DeleteProjectArtifactsAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (projectId == Guid.Empty)
            {
                throw new ArgumentException(Texts.ProjectIdentifierRequired, nameof(projectId));
            }

            if (storageActivity.IsCleanupRunning)
            {
                throw new InvalidOperationException(Texts.ProjectCannotDeletedDuringVersionCleanup);
            }

            var root = await GetEffectiveRootAsync(cancellationToken);
            var projectDirectory = pathPolicy.ResolvePath(root, projectId.ToString("N"));

            if (!Directory.Exists(projectDirectory))
            {
                return;
            }

            pathPolicy.EnsureTreeHasNoReparsePoints(new DirectoryInfo(projectDirectory));
            Directory.Delete(projectDirectory, recursive: true);
        }

        public async Task DeleteArtifactAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = await ResolvePathAsync(relativePath, cancellationToken);
            var attributes = pathPolicy.GetExistingAttributes(path);
            if (!attributes.HasValue)
            {
                return;
            }

            if ((attributes.Value & FileAttributes.Directory) != 0)
            {
                throw new InvalidOperationException(Texts.VersionArchivePathInvalid);
            }

            pathPolicy.EnsureNotReparsePoint(path);
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

        private long CalculateDirectorySize(string root, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            var directory = new DirectoryInfo(root);
            pathPolicy.EnsureTreeHasNoReparsePoints(directory);
            var total = 0L;

            foreach (var file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total = checked(total + file.Length);
            }

            return total;
        }
    }
}
