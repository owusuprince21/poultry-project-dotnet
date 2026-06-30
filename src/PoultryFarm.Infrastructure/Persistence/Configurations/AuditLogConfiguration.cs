using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoultryFarm.Domain.Audit;

namespace PoultryFarm.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActorUsername).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ActorRole).HasMaxLength(80).IsRequired();
        builder.Property(x => x.CompanyName).HasMaxLength(200);
        builder.Property(x => x.Action).HasMaxLength(120).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.TargetName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TargetUsername).HasMaxLength(128);
        builder.Property(x => x.Detail).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(80).IsRequired();

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.ActorUserId);
    }
}
