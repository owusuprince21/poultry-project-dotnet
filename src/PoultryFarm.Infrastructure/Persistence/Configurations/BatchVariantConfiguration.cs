using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoultryFarm.Domain.Batches;

namespace PoultryFarm.Infrastructure.Persistence.Configurations;

public sealed class BatchVariantConfiguration : IEntityTypeConfiguration<BatchVariant>
{
    public void Configure(EntityTypeBuilder<BatchVariant> builder)
    {
        builder.ToTable("BatchVariants");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Color).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(255);
        builder.HasIndex(x => new { x.BatchId, x.Color }).IsUnique();
    }
}
