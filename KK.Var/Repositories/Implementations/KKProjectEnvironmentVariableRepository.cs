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

public sealed class KKProjectEnvironmentVariableRepository(
    IDbContextFactory<AppDbContext> contextFactory)
    : IKKProjectEnvironmentVariableRepository
{
    public async Task<IReadOnlyList<KKProjectEnvironmentVariable>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ProjectEnvironmentVariables
            .AsNoTracking()
            .Where(variable => variable.KKProjectId == projectId)
            .OrderBy(variable => variable.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceAsync(
        Guid projectId,
        EnvironmentFileFormat format,
        IReadOnlyCollection<KKProjectEnvironmentVariable> variables,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var project = await db.Projects.SingleOrDefaultAsync(
                candidate => candidate.Id == projectId,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var existing = await db.ProjectEnvironmentVariables
            .Where(variable => variable.KKProjectId == projectId)
            .ToListAsync(cancellationToken);

        db.ProjectEnvironmentVariables.RemoveRange(existing);
        db.ProjectEnvironmentVariables.AddRange(variables);
        project.EnvironmentFileFormat = format;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
