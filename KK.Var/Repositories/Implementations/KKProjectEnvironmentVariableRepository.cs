using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class KKProjectEnvironmentVariableRepository(
        IDbContextFactory<AppDbContext> contextFactory,
        ILocalizationService localizationService)
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
                cancellationToken) ??
                    throw new KeyNotFoundException(localizationService.Format(Texts.ProjectNotFound2, projectId));

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
}
