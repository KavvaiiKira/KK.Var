using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;
using KK.Var.Models;
using Microsoft.EntityFrameworkCore;

namespace KK.Var.Repositories.Implementations;

public sealed class KKProjectRepository(
    IDbContextFactory<AppDbContext> contextFactory) : IKKProjectRepository
{
    public async Task<IReadOnlyList<KKProject>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Projects
            .AsNoTracking()
            .Include(project => project.Versions)
            .Include(project => project.Deployments)
            .OrderBy(project => project.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<KKProject?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Projects
            .AsNoTracking()
            .Include(project => project.EnvironmentVariables)
            .SingleOrDefaultAsync(project => project.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(
        string name,
        Guid? excludedProjectId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Projects.AnyAsync(
            project => project.Name == name &&
                       (!excludedProjectId.HasValue || project.Id != excludedProjectId.Value),
            cancellationToken);
    }

    public async Task<bool> ServiceNameExistsAsync(
        string serviceName,
        Guid? excludedProjectId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Projects.AnyAsync(
            project => project.RemoteServiceName == serviceName &&
                       (!excludedProjectId.HasValue || project.Id != excludedProjectId.Value),
            cancellationToken);
    }

    public async Task AddAsync(
        KKProject project,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Projects.Add(project);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        KKProject project,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Projects.SingleOrDefaultAsync(
            candidate => candidate.Id == project.Id,
            cancellationToken);

        if (existing is null)
        {
            throw new KeyNotFoundException($"Project '{project.Id}' was not found.");
        }

        db.Entry(existing).CurrentValues.SetValues(project);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);

        if (project is null)
        {
            return;
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);
    }
}
