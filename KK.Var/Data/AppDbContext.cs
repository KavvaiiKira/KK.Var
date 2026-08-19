using KK.Var.Models;
using Microsoft.EntityFrameworkCore;

namespace KK.Var.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<KKProject> Projects => Set<KKProject>();

    public DbSet<KKProjectEnvironmentVariable> ProjectEnvironmentVariables =>
        Set<KKProjectEnvironmentVariable>();

    public DbSet<KKProjectVersion> ProjectVersions => Set<KKProjectVersion>();

    public DbSet<KKProjectDeployment> ProjectDeployments =>
        Set<KKProjectDeployment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
