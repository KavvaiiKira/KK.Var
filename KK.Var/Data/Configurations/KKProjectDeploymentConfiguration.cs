using KK.Var.Constants;
using KK.Var.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KK.Var.Data.Configurations
{
    public sealed class KKProjectDeploymentConfiguration
        : IEntityTypeConfiguration<KKProjectDeployment>
    {
        public void Configure(EntityTypeBuilder<KKProjectDeployment> builder)
        {
            builder.ToTable(DatabaseSchema.DeploymentsTable);

            builder.HasKey(deployment => deployment.Id);

            builder.HasIndex(deployment => deployment.OperationSequence).IsUnique();

            builder.Property(deployment => deployment.OperationType)
                .HasConversion<int>();

            builder.Property(deployment => deployment.Status)
                .HasConversion<int>();

            builder.Property(deployment => deployment.Stage)
                .HasConversion<int>();

            builder.Property(deployment => deployment.UnitChange)
                .HasConversion<int>();

            builder.Property(deployment => deployment.RemoteOperationId)
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(deployment => deployment.VersionTag)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(deployment => deployment.VersionDescription)
                .HasMaxLength(2000);

            builder.Property(deployment => deployment.SourceCommitSha)
                .HasMaxLength(64);

            builder.Property(deployment => deployment.VariablesSnapshotJson)
                .IsRequired();

            builder.Property(deployment => deployment.LogPath)
                .HasMaxLength(1024);

            builder.Property(deployment => deployment.ErrorMessage)
                .HasMaxLength(4000);

            builder.Property(deployment => deployment.RecoveryAttemptsJson)
                .IsRequired();

            builder.HasIndex(deployment => new
            {
                deployment.KKProjectId,
                deployment.StartedAtUtc,
            });

            builder.HasIndex(deployment => deployment.StartedAtUtc);
        }
    }
}
