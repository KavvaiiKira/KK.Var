using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class VersionCleanupService(
    IKKProjectRepository projectRepository,
    IKKProjectVersionRepository versionRepository,
    IKKProjectVersionService versionService,
    IArtifactStorageService artifactStorageService,
    IDeploymentOperationQueue operationQueue,
    ILocalizationService localizationService) : IVersionCleanupService
{
    private static readonly Regex ArchivePathPattern = new Regex(
        @"\A[0-9a-f]{32}/[A-Za-z0-9][A-Za-z0-9._-]{0,199}\.tar\.gz\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<VersionCleanupPlan> PlanAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        EnsureManualCleanupAvailable();

        await using var lease = await artifactStorageService.AcquireCleanupLeaseAsync(
            cancellationToken);

        if (operationQueue.HasActiveOperations())
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя очищать версии во время активной операции."));
        }

        return await BuildPlanAsync(projectId, cancellationToken);
    }

    public async Task<VersionCleanupPlan> ExecuteAsync(
        Guid projectId,
        VersionCleanupPlan? expectedPlan = null,
        bool afterSuccessfulDeploy = false,
        CancellationToken cancellationToken = default)
    {
        if (!afterSuccessfulDeploy)
        {
            EnsureManualCleanupAvailable();
        }

        await using var lease = await artifactStorageService.AcquireCleanupLeaseAsync(
            cancellationToken);

        if (!afterSuccessfulDeploy && operationQueue.HasActiveOperations())
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя очищать версии во время активной операции."));
        }

        var plan = await BuildPlanAsync(projectId, cancellationToken);

        if (expectedPlan is not null &&
            (expectedPlan.ProjectId != plan.ProjectId ||
             !string.Equals(
                 expectedPlan.ArtifactRoot,
                 plan.ArtifactRoot,
                 StringComparison.OrdinalIgnoreCase) ||
             !expectedPlan.Items.SequenceEqual(plan.Items)))
        {
            throw new InvalidOperationException(localizationService.Get(
                "План очистки изменился. Рассчитайте его повторно."));
        }

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await artifactStorageService.DeleteArtifactAsync(
                    item.ArtifactRelativePath,
                    cancellationToken);
                await versionRepository.DeleteAsync(item.VersionId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new InvalidOperationException(localizationService.Format(
                    "Очистка остановлена на версии «{0}»: {1}",
                    item.ArtifactRelativePath,
                    exception.Message), exception);
            }
        }

        return plan;
    }

    private async Task<VersionCleanupPlan> BuildPlanAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
            throw new KeyNotFoundException(localizationService.Get("Проект не найден."));
        if (project.MaxStoredVersions <= 0 ||
            project.MaxStoredVersionBytes is <= 0)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Политика хранения проекта недопустима."));
        }

        var artifactRoot = await artifactStorageService.GetEffectiveRootAsync(cancellationToken);
        var versions = (await versionRepository.GetByProjectIdAsync(projectId, cancellationToken))
            .OrderBy(version => version.CreatedAtUtc)
            .ThenBy(version => version.Id)
            .ToArray();
        var protectedIds = await versionService.GetProtectedVersionIdsAsync(
            projectId,
            cancellationToken);

        var candidates = new List<(KKProjectVersion Version, long Bytes)>();
        var totalBytes = 0L;

        foreach (var version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateArchivePath(projectId, version.ArtifactRelativePath);

            var state = await artifactStorageService.GetFileStateAsync(
                version.ArtifactRelativePath,
                cancellationToken);
            if (System.IO.Directory.Exists(state.AbsolutePath))
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Путь к архиву версии недопустим."));
            }

            totalBytes = checked(totalBytes + state.Size);

            if (!version.IsPinned && !protectedIds.Contains(version.Id))
            {
                candidates.Add((version, state.Size));
            }
        }

        var remainingCount = versions.Length;
        var items = new List<VersionCleanupItem>();
        var bytesToDelete = 0L;

        foreach (var candidate in candidates)
        {
            if (remainingCount <= project.MaxStoredVersions &&
                (!project.MaxStoredVersionBytes.HasValue ||
                 totalBytes <= project.MaxStoredVersionBytes.Value))
            {
                break;
            }

            items.Add(new VersionCleanupItem(
                candidate.Version.Id,
                candidate.Version.ArtifactRelativePath,
                candidate.Bytes));
            bytesToDelete = checked(bytesToDelete + candidate.Bytes);
            totalBytes -= candidate.Bytes;
            remainingCount--;
        }

        return new VersionCleanupPlan(projectId, artifactRoot, items, bytesToDelete);
    }

    private void EnsureManualCleanupAvailable()
    {
        if (operationQueue.HasActiveOperations() ||
            artifactStorageService.IsMigrationRunning ||
            artifactStorageService.IsCleanupRunning)
        {
            throw new InvalidOperationException(localizationService.Get(
                "Нельзя очищать версии во время активной операции."));
        }
    }

    private void ValidateArchivePath(Guid projectId, string? relativePath)
    {
        var normalizedPath = relativePath?.Replace('\\', '/');

        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            !ArchivePathPattern.IsMatch(normalizedPath) ||
            !normalizedPath.StartsWith(
                projectId.ToString("N") + "/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(localizationService.Get(
                "Путь к архиву версии недопустим."));
        }
    }
}
