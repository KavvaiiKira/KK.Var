using KK.Var.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KK.Var.Data.Configurations;

public sealed class KKProjectVersionConfiguration
    : IEntityTypeConfiguration<KKProjectVersion>
{
    public void Configure(EntityTypeBuilder<KKProjectVersion> builder)
    {
        builder.ToTable("KKProjectVersions", table =>
        {
            table.HasCheckConstraint(
                "CK_KKProjectVersions_ArtifactSize",
                "ArtifactSize >= 0");
        });

        builder.HasKey(version => version.Id);

        builder.Property(version => version.Tag)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("NOCASE");

        builder.HasIndex(version => new { version.KKProjectId, version.Tag })
            .IsUnique();

        builder.Property(version => version.ArtifactRelativePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(version => version.ArtifactSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(version => version.SourceCommitSha)
            .HasMaxLength(64);

        builder.Property(version => version.Description)
            .HasMaxLength(2000);

        builder.HasMany(version => version.Deployments)
            .WithOne(deployment => deployment.Version)
            .HasForeignKey(deployment => deployment.KKProjectVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
