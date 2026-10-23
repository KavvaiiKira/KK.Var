using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class KKProjectVersionService(
        IKKProjectRepository projectRepository,
        IKKProjectVersionRepository versionRepository,
        IKKProjectDeploymentRepository deploymentRepository,
        IArtifactStorageService artifactStorageService,
        IArtifactStorageActivity storageActivity,
        IDeploymentOperationQueue operationQueue,
        ILocalizationService localizationService) : IKKProjectVersionService
    {
        private const string Sha256ValidationPattern = "^[0-9a-fA-F]{64}$";

        private const string CommitShaValidationPattern = "^[0-9a-fA-F]{40,64}$";

        private static readonly Regex Sha256Pattern = new Regex(
            Sha256ValidationPattern,
            RegexOptions.CultureInvariant);

        private static readonly Regex CommitShaPattern = new Regex(
            CommitShaValidationPattern,
            RegexOptions.CultureInvariant);

        public Task<IReadOnlyList<KKProjectVersion>> GetByProjectIdAsync(
            Guid projectId,
            CancellationToken cancellationToken = default) =>
            versionRepository.GetByProjectIdAsync(projectId, cancellationToken);

        public async Task<KKProjectVersion> CreateAsync(
            KKProjectVersion version,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(version);

            _ = await projectRepository.GetByIdAsync(version.KKProjectId, cancellationToken) ??
                throw new KeyNotFoundException(localizationService.Format(Texts.ProjectNotFound2, version.KKProjectId));

            version.Tag = Required(version.Tag, nameof(version.Tag), 200);
            version.TargetArchitecture = ArchitectureCompatibility.Normalize(version.TargetArchitecture);
            if (string.IsNullOrWhiteSpace(version.TargetArchitecture) || version.TargetArchitecture == ArchitectureNames.Unknown)
            {
                throw new ArgumentException(Texts.SpecifyVersionSTargetArchitecture);
            }
            version.ArtifactRelativePath = Required(
                version.ArtifactRelativePath,
                nameof(version.ArtifactRelativePath),
                1024);
            version.ArtifactSha256 = Required(
                version.ArtifactSha256,
                nameof(version.ArtifactSha256),
                64).ToLowerInvariant();
            version.SourceCommitSha = string.IsNullOrWhiteSpace(version.SourceCommitSha) ?
                null :
                version.SourceCommitSha.Trim().ToLowerInvariant();
            version.Description = string.IsNullOrWhiteSpace(version.Description) ?
                null :
                version.Description.Trim();

            ValidateRelativePath(version.ArtifactRelativePath);

            if (!Sha256Pattern.IsMatch(version.ArtifactSha256))
            {
                throw new ArgumentException(Texts.InvalidArtifactSHA256Checksum);
            }

            if (version.SourceCommitSha is not null &&
                !CommitShaPattern.IsMatch(version.SourceCommitSha))
            {
                throw new ArgumentException(Texts.InvalidSourceCommitSHA);
            }

            if (version.ArtifactSize < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(version.ArtifactSize),
                    Texts.ArtifactSizeCannotNegative);
            }

            if (await versionRepository.TagExistsAsync(
                version.KKProjectId,
                version.Tag,
                cancellationToken))
            {
                throw new VersionTagException(VersionTagError.AlreadyExists, localizationService.Format(
                    Texts.VersionAlreadyExists,
                    version.Tag));
            }

            version.Id = version.Id == Guid.Empty ? Guid.NewGuid() : version.Id;
            version.CreatedAtUtc = DateTime.UtcNow;

            await versionRepository.AddAsync(version, cancellationToken);

            return version;
        }

        public async Task<IReadOnlySet<Guid>> GetProtectedVersionIdsAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
        {
            var deployments = await deploymentRepository.GetByProjectIdAsync(
                projectId,
                cancellationToken);

            return deployments
                .Where(deployment =>
                    deployment.Status == Enums.DeploymentStatus.Succeeded &&
                    deployment.KKProjectVersionId.HasValue)
                .OrderByDescending(deployment => deployment.CompletedAtUtc)
                .Select(deployment => deployment.KKProjectVersionId!.Value)
                .Distinct()
                .Take(2)
                .ToHashSet();
        }

        public async Task SetPinnedAsync(
            Guid versionId,
            bool isPinned,
            CancellationToken cancellationToken = default)
        {
            await using var lease = storageActivity.AcquireDestructiveLease(cancellationToken);
            if (storageActivity.IsCleanupRunning)
            {
                throw new InvalidOperationException(Texts.VersionsCannotChangedDuringCleanup);
            }

            var version = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
                throw new KeyNotFoundException(Texts.VersionNotFound);

            version.IsPinned = isPinned;
            await versionRepository.UpdateAsync(version, cancellationToken);
        }

        public async Task DeleteAsync(
            Guid versionId,
            CancellationToken cancellationToken = default)
        {
            await using var lease = storageActivity.AcquireDestructiveLease(cancellationToken);
            var version = await versionRepository.GetByIdAsync(versionId, cancellationToken) ??
                throw new KeyNotFoundException(Texts.VersionNotFound);

            if (operationQueue.HasActiveOperation(version.KKProjectId) ||
                storageActivity.IsMigrationRunning ||
                storageActivity.IsCleanupRunning)
            {
                throw new InvalidOperationException(Texts.VersionCannotDeletedDuringActiveOperation);
            }

            var protectedIds = await GetProtectedVersionIdsAsync(
                version.KKProjectId,
                cancellationToken);
            if (protectedIds.Contains(version.Id))
            {
                throw new InvalidOperationException(Texts.CurrentPreviousDeployedVersionsCannotDeleted);
            }

            await artifactStorageService.DeleteArtifactAsync(
                version.ArtifactRelativePath,
                cancellationToken);
            await versionRepository.DeleteAsync(version.Id, cancellationToken);
        }

        private string Required(string? value, string parameterName, int maxLength)
        {
            var normalized = value?.Trim();

            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException(Texts.ValueRequired, parameterName);
            }

            if (normalized.Length > maxLength)
            {
                throw new ArgumentException(
                    localizationService.Format(Texts.ValueCannotExceedCharacters, maxLength),
                    parameterName);
            }

            return normalized;
        }

        private void ValidateRelativePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                throw new ArgumentException(Texts.ArtifactPathMustRelative);
            }

            foreach (var segment in path.Replace('\\', '/').Split('/'))
            {
                if (segment is "." or "..")
                {
                    throw new ArgumentException(Texts.ArtifactPathCannotContainSegments);
                }
            }
        }
    }
}
