using KK.Var.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KK.Var.Data.Configurations;

public sealed class KKProjectConfiguration : IEntityTypeConfiguration<KKProject>
{
    public void Configure(EntityTypeBuilder<KKProject> builder)
    {
        builder.ToTable("KKProjects", table =>
        {
            table.HasCheckConstraint(
                "CK_KKProjects_Source",
                "(SourceType = 1 AND LocalDirectoryPath IS NOT NULL " +
                "AND GitHubRepositoryId IS NULL AND GitHubRepositoryFullName IS NULL " +
                "AND GitHubCloneUrl IS NULL) OR " +
                "(SourceType = 2 AND LocalDirectoryPath IS NULL " +
                "AND GitHubRepositoryId IS NOT NULL AND GitHubRepositoryFullName IS NOT NULL " +
                "AND GitHubCloneUrl IS NOT NULL)");
        });

        builder.HasKey(project => project.Id);

        builder.Property(project => project.Name)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("NOCASE");

        builder.HasIndex(project => project.Name)
            .IsUnique();

        builder.Property(project => project.Description)
            .HasMaxLength(2000);

        builder.Property(project => project.SourceType)
            .HasConversion<int>();

        builder.Property(project => project.LocalDirectoryPath)
            .HasMaxLength(1024);

        builder.Property(project => project.GitHubRepositoryFullName)
            .HasMaxLength(300);

        builder.Property(project => project.GitHubCloneUrl)
            .HasMaxLength(2048);

        builder.Property(project => project.BuildProvider)
            .HasConversion<int>();

        builder.Property(project => project.BuildConfigurationJson)
            .IsRequired();

        builder.Property(project => project.RemoteServiceName)
            .IsRequired()
            .HasMaxLength(255)
            .UseCollation("NOCASE");

        builder.HasIndex(project => project.RemoteServiceName)
            .IsUnique();

        builder.Property(project => project.RemoteExecutableFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(project => project.RemoteDeploymentDirectory)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(project => project.ProjectEnvironmentFilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.HasMany(project => project.EnvironmentVariables)
            .WithOne(variable => variable.Project)
            .HasForeignKey(variable => variable.KKProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(project => project.Versions)
            .WithOne(version => version.Project)
            .HasForeignKey(version => version.KKProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(project => project.Deployments)
            .WithOne(deployment => deployment.Project)
            .HasForeignKey(deployment => deployment.KKProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
