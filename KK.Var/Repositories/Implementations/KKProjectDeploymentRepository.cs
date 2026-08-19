using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;
using KK.Var.Enums;
using KK.Var.Models;
using Microsoft.EntityFrameworkCore;

namespace KK.Var.Repositories.Implementations;

public sealed class KKProjectDeploymentRepository(
    IDbContextFactory<AppDbContext> contextFactory) : IKKProjectDeploymentRepository
{
    public async Task<IReadOnlyList<KKProjectDeployment>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ProjectDeployments
            .AsNoTracking()
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

    public async Task AddAsync(
        KKProjectDeployment deployment,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.ProjectDeployments.Add(deployment);
        await db.SaveChangesAsync(cancellationToken);
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
            throw new KeyNotFoundException($"Deployment '{deployment.Id}' was not found.");
        }

        db.Entry(existing).CurrentValues.SetValues(deployment);
        await db.SaveChangesAsync(cancellationToken);
    }
}
