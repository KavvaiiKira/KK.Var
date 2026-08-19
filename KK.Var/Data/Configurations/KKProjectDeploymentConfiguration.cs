using KK.Var.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KK.Var.Data.Configurations;

public sealed class KKProjectDeploymentConfiguration
    : IEntityTypeConfiguration<KKProjectDeployment>
{
    public void Configure(EntityTypeBuilder<KKProjectDeployment> builder)
    {
        builder.ToTable("KKProjectDeployments");

        builder.HasKey(deployment => deployment.Id);

        builder.Property(deployment => deployment.OperationType)
            .HasConversion<int>();

        builder.Property(deployment => deployment.Status)
            .HasConversion<int>();

        builder.Property(deployment => deployment.VariablesSnapshotJson)
            .IsRequired();

        builder.Property(deployment => deployment.LogPath)
            .HasMaxLength(1024);

        builder.Property(deployment => deployment.ErrorMessage)
            .HasMaxLength(4000);

        builder.HasIndex(deployment => new
        {
            deployment.KKProjectId,
            deployment.StartedAtUtc,
        });
    }
}
