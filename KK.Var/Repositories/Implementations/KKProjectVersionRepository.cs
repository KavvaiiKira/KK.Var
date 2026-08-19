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
}
