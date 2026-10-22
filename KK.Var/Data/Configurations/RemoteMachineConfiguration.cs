using KK.Var.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KK.Var.Data.Configurations
{
    public sealed class RemoteMachineConfiguration : IEntityTypeConfiguration<RemoteMachine>
    {
        public void Configure(EntityTypeBuilder<RemoteMachine> builder)
        {
            builder.HasKey(machine => machine.Id);
            builder.Property(machine => machine.AuthenticationMethod).HasConversion<int>();
            builder.Property(machine => machine.Name).IsRequired().HasMaxLength(200);
            builder.Property(machine => machine.Host).IsRequired().HasMaxLength(1024);
            builder.Property(machine => machine.UserName).IsRequired().HasMaxLength(255);
            builder.Property(machine => machine.PrivateKeyPath).HasMaxLength(2048);
            builder.HasMany(machine => machine.Projects).WithOne(project => project.RemoteMachine)
                .HasForeignKey(project => project.RemoteMachineId).OnDelete(DeleteBehavior.Restrict);
            builder.HasMany(machine => machine.Deployments).WithOne(deployment => deployment.RemoteMachine)
                .HasForeignKey(deployment => deployment.RemoteMachineId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
