using KK.Var.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KK.Var.Data.Configurations;

public sealed class KKProjectEnvironmentVariableConfiguration
    : IEntityTypeConfiguration<KKProjectEnvironmentVariable>
{
    public void Configure(EntityTypeBuilder<KKProjectEnvironmentVariable> builder)
    {
        builder.ToTable("KKProjectEnvironmentVariables");

        builder.HasKey(variable => variable.Id);

        builder.Property(variable => variable.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(variable => variable.Value)
            .IsRequired();

        builder.HasIndex(variable => new { variable.KKProjectId, variable.Name })
            .IsUnique();
    }
}
