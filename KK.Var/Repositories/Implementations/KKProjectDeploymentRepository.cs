using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;
using Microsoft.EntityFrameworkCore;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Repositories.Implementations
{
    public sealed class KKProjectDeploymentRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILocalizationService localizationService) : IKKProjectDeploymentRepository
    {
        public async Task<IReadOnlyList<KKProjectDeployment>> SearchAsync(
            string? projectName,
            string? searchText,
            DateTime? startedFromUtc,
            DateTime? startedBeforeUtc,
            DeploymentStatus? status,
            int skip,
            int take,
            CancellationToken cancellationToken = default,
            Guid? remoteMachineId = null)
        {
            if (skip < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip));
            }

            if (take is < 1 or > 200)
            {
                throw new ArgumentOutOfRangeException(nameof(take));
            }

            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

            var query = db.ProjectDeployments
                .AsNoTracking()
                .Include(deployment => deployment.Project)
                .Include(deployment => deployment.Version)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(projectName))
            {
                query = query.Where(deployment => deployment.Project.Name == projectName);
            }

            if (status.HasValue)
            {
                query = query.Where(deployment => deployment.Status == status.Value);
            }

            if (remoteMachineId.HasValue)
            {
                query = query.Where(deployment => deployment.RemoteMachineId == remoteMachineId);
            }

            if (startedFromUtc.HasValue && startedBeforeUtc.HasValue)
            {
                query = query.Where(deployment =>
                    deployment.StartedAtUtc >= startedFromUtc.Value &&
                    deployment.StartedAtUtc < startedBeforeUtc.Value);
            }
            else if (!string.IsNullOrWhiteSpace(searchText))
            {
                var normalizedSearch = searchText.Trim();
                query = query.Where(deployment =>
                    deployment.Project.Name.Contains(normalizedSearch) ||
                    deployment.VersionTag.Contains(normalizedSearch));
            }

            return await query
                .OrderByDescending(deployment => deployment.StartedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

            return await db.ProjectDeployments
                .AsNoTracking()
                .Include(deployment => deployment.Version)
                .Where(deployment => deployment.KKProjectId == projectId)
                .OrderByDescending(deployment => deployment.StartedAtUtc)
                .ToListAsync(cancellationToken);
        }

        public async Task<KKProjectDeployment?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

            return await db.ProjectDeployments
                .AsNoTracking()
                .Include(deployment => deployment.Project)
                .SingleOrDefaultAsync(deployment => deployment.Id == id, cancellationToken);
        }

        public async Task<KKProjectDeployment?> GetLatestSuccessfulAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

            return await db.ProjectDeployments
                .AsNoTracking()
                .Where(deployment =>
                    deployment.KKProjectId == projectId &&
                    deployment.Status == DeploymentStatus.Succeeded)
                .OrderByDescending(deployment => deployment.CompletedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<KKProjectDeployment>> GetRunningAsync(
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

            return await db.ProjectDeployments
                .AsNoTracking()
                .Include(deployment => deployment.Project)
                .Include(deployment => deployment.Version)
                .Where(deployment => deployment.Status == DeploymentStatus.Running)
                .OrderBy(deployment => deployment.StartedAtUtc)
                .ToListAsync(cancellationToken);
        }

        public async Task AddAsync(
            KKProjectDeployment deployment,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            deployment.OperationSequence = (await db.ProjectDeployments.MaxAsync(item => (long?)item.OperationSequence, cancellationToken) ?? 0) + 1;
            db.ProjectDeployments.Add(deployment);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task UpdateAsync(
            KKProjectDeployment deployment,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await db.ProjectDeployments.SingleOrDefaultAsync(
                candidate => candidate.Id == deployment.Id,
                cancellationToken);

            if (existing is null)
            {
                throw new KeyNotFoundException(localizationService.Format(Texts.DeploymentNotFound, deployment.Id));
            }

            var sequence = existing.OperationSequence;
            db.Entry(existing).CurrentValues.SetValues(deployment);
            existing.OperationSequence = sequence;
            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task BeginRecoveryAsync(Guid deploymentId, DeploymentRecoveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var deployment = await db.ProjectDeployments.SingleAsync(item => item.Id == deploymentId, cancellationToken);
            if (deployment.Status != DeploymentStatus.Failed || !deployment.RecoveryRequired)
            {
                throw new InvalidOperationException(Texts.RecoveryNotRequired);
            }
            if (await HasNewerOperationAsync(db, deployment, cancellationToken))
            {
                throw new InvalidOperationException(Texts.RecoverySuperseded);
            }
            var attempts = DeploymentRecoveryAttempt.Read(deployment.RecoveryAttemptsJson)
                .Select(item => item.Status == DeploymentRecoveryAttemptStatus.Running ?
                    item with { Status = DeploymentRecoveryAttemptStatus.Interrupted, CompletedAtUtc = DateTime.UtcNow } :
                    item).ToList();
            attempts.Add(attempt);
            deployment.RecoveryAttemptsJson = JsonSerializer.Serialize(attempts);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task<bool> IsRecoveryCurrentAsync(Guid deploymentId, CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var deployment = await db.ProjectDeployments.SingleAsync(item => item.Id == deploymentId, cancellationToken);
            return !await HasNewerOperationAsync(db, deployment, cancellationToken);
        }

        private static async Task<bool> HasNewerOperationAsync(AppDbContext db, KKProjectDeployment deployment, CancellationToken cancellationToken)
        {
            var host = deployment.RemoteHost.ToLowerInvariant();
            var directory = RemotePaths.NormalizeDirectoryForComparison(deployment.RemoteDirectorySnapshot);
            var candidates = await db.ProjectDeployments.Where(item => item.OperationSequence > deployment.OperationSequence &&
                (item.KKProjectId == deployment.KKProjectId ||
                 item.RemoteMachineId == deployment.RemoteMachineId ||
                 (item.RemoteHost.ToLower() == host && item.RemotePort == deployment.RemotePort)))
                .Select(item => new { item.KKProjectId, item.RemoteDirectorySnapshot, item.RemoteServiceNameSnapshot })
                .ToListAsync(cancellationToken);
            return candidates.Any(item => item.KKProjectId == deployment.KKProjectId ||
                string.Equals(item.RemoteServiceNameSnapshot, deployment.RemoteServiceNameSnapshot, StringComparison.Ordinal) ||
                DirectoriesOverlap(directory, RemotePaths.NormalizeDirectoryForComparison(item.RemoteDirectorySnapshot)));
        }

        private static bool DirectoriesOverlap(string first, string second) =>
            string.Equals(first, second, StringComparison.Ordinal) ||
            first.StartsWith(second + "/", StringComparison.Ordinal) ||
            second.StartsWith(first + "/", StringComparison.Ordinal);

        public async Task CompleteRecoveryAsync(Guid deploymentId, DeploymentRecoveryAttempt attempt, CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var deployment = await db.ProjectDeployments.SingleAsync(item => item.Id == deploymentId, cancellationToken);
            var attempts = DeploymentRecoveryAttempt.Read(deployment.RecoveryAttemptsJson).ToList();
            var index = attempts.FindIndex(item => item.Id == attempt.Id && item.Status == DeploymentRecoveryAttemptStatus.Running);
            if (index < 0 || attempt.Status == DeploymentRecoveryAttemptStatus.Running ||
                (attempt.Status == DeploymentRecoveryAttemptStatus.Succeeded &&
                 attempt.Outcome is not (DeploymentRecoveryOutcome.PreviousVersionRestored or
                     DeploymentRecoveryOutcome.NewVersionRemoved or DeploymentRecoveryOutcome.PreviousFilesRestored or DeploymentRecoveryOutcome.StagingCleaned)))
            {
                throw new InvalidOperationException(Texts.RecoveryNotRequired);
            }
            attempts[index] = attempt;
            deployment.RecoveryAttemptsJson = JsonSerializer.Serialize(attempts);
            if (attempt.Status == DeploymentRecoveryAttemptStatus.Succeeded)
            {
                deployment.RecoveryRequired = false;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
