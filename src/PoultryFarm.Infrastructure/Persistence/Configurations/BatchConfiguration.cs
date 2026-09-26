using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoultryFarm.Domain.Batches;

namespace PoultryFarm.Infrastructure.Persistence.Configurations;

public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        builder.ToTable("Batches");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.BatchNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Breed).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Supplier).HasMaxLength(200);
        builder.Property(x => x.BirdType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.CompanyId, x.BatchNumber }).IsUnique();
        builder.HasMany(x => x.Variants).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId);
    }
}
