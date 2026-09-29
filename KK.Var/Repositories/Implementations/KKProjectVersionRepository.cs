using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;
using KK.Var.Models;
using Microsoft.EntityFrameworkCore;

namespace KK.Var.Repositories.Implementations;

public sealed class KKProjectVersionRepository(
    IDbContextFactory<AppDbContext> contextFactory) : IKKProjectVersionRepository
{
    public async Task<IReadOnlyList<KKProjectVersion>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ProjectVersions
            .AsNoTracking()
            .OrderBy(version => version.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KKProjectVersion>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ProjectVersions
            .AsNoTracking()
            .Where(version => version.KKProjectId == projectId)
            .OrderByDescending(version => version.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<KKProjectVersion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ProjectVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(version => version.Id == id, cancellationToken);
    }

    public async Task<bool> TagExistsAsync(
        Guid projectId,
        string tag,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ProjectVersions.AnyAsync(
            version => version.KKProjectId == projectId && version.Tag == tag,
            cancellationToken);
    }

    public async Task AddAsync(
        KKProjectVersion version,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.ProjectVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        KKProjectVersion version,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.ProjectVersions.SingleOrDefaultAsync(
            candidate => candidate.Id == version.Id,
            cancellationToken) ??
            throw new KeyNotFoundException($"Version '{version.Id}' was not found.");

        db.Entry(existing).CurrentValues.SetValues(version);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var version = await db.ProjectVersions.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);

        if (version is null)
        {
            return;
        }

        db.ProjectVersions.Remove(version);
        await db.SaveChangesAsync(cancellationToken);
    }
}
