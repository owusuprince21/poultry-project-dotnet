using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoultryFarm.Domain.Feed;
using PoultryFarm.Domain.Health;
using PoultryFarm.Domain.Operations;
using PoultryFarm.Domain.Production;
using PoultryFarm.Domain.Sales;
using PoultryFarm.Domain.Schedules;

namespace PoultryFarm.Infrastructure.Persistence.Configurations;

public sealed class DailyObservationConfiguration : IEntityTypeConfiguration<DailyObservation>
{
    public void Configure(EntityTypeBuilder<DailyObservation> builder)
    {
        builder.ToTable("DailyObservations");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Recommendation).HasMaxLength(4000);
        builder.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Date });
    }
}

public sealed class EggProductionConfiguration : IEntityTypeConfiguration<EggProduction>
{
    public void Configure(EntityTypeBuilder<EggProduction> builder)
    {
        builder.ToTable("EggProductions");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.EggColor).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CollectionType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CollectionPeriod).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => new { x.BatchVariantId, x.Date, x.CollectionPeriod, x.CollectionType, x.EggColor }).IsUnique();
    }
}

public sealed class FeedStockConfiguration : IEntityTypeConfiguration<FeedStock>
{
    public void Configure(EntityTypeBuilder<FeedStock> builder)
    {
        builder.ToTable("FeedStocks");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.FeedType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CurrentStockKg).HasPrecision(12, 2);
        builder.Property(x => x.MinimumThresholdKg).HasPrecision(12, 2);
        builder.Property(x => x.CostPerKg).HasPrecision(12, 2);
        builder.HasIndex(x => new { x.CompanyId, x.FeedType }).IsUnique();
    }
}

public sealed class FeedConfigurationConfiguration : IEntityTypeConfiguration<FeedConfiguration>
{
    public void Configure(EntityTypeBuilder<FeedConfiguration> builder)
    {
        builder.ToTable("FeedConfigurations");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Name, x.BagSizeKg }).IsUnique();
    }
}

public sealed class FeedStockLotConfiguration : IEntityTypeConfiguration<FeedStockLot>
{
    public void Configure(EntityTypeBuilder<FeedStockLot> builder)
    {
        builder.ToTable("FeedStockLots");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.FeedType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Supplier).HasMaxLength(200);
        builder.Property(x => x.CostPerBag).HasPrecision(12, 2);
        builder.HasIndex(x => new { x.CompanyId, x.FeedType, x.BagSizeKg });
    }
}

public sealed class FeedConsumptionConfiguration : IEntityTypeConfiguration<FeedConsumption>
{
    public void Configure(EntityTypeBuilder<FeedConsumption> builder)
    {
        builder.ToTable("FeedConsumptions");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.FeedType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CollectionPeriod).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.AmountKg).HasPrecision(12, 2);
        builder.Property(x => x.CostPerKg).HasPrecision(12, 2);
        builder.Property(x => x.Supplier).HasMaxLength(200);
        builder.HasIndex(x => new { x.BatchVariantId, x.Date, x.FeedConfigurationId, x.CollectionPeriod }).IsUnique();
    }
}

public sealed class EggSaleConfiguration : IEntityTypeConfiguration<EggSale>
{
    public void Configure(EntityTypeBuilder<EggSale> builder)
    {
        builder.ToTable("EggSales");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.BuyerName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ReceiptId).HasMaxLength(40).IsRequired();
        builder.Property(x => x.GrandTotal).HasPrecision(14, 2);
        builder.Property(x => x.PaymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.ReceiptId).IsUnique();
        builder.HasMany(x => x.Items).WithOne(x => x.EggSale).HasForeignKey(x => x.EggSaleId);
    }
}

public sealed class EggSaleItemConfiguration : IEntityTypeConfiguration<EggSaleItem>
{
    public void Configure(EntityTypeBuilder<EggSaleItem> builder)
    {
        builder.ToTable("EggSaleItems");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.EggColor).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Size).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.UnitPrice).HasPrecision(14, 2);
        builder.Property(x => x.LineTotal).HasPrecision(14, 2);
    }
}

public sealed class BirdSaleConfiguration : IEntityTypeConfiguration<BirdSale>
{
    public void Configure(EntityTypeBuilder<BirdSale> builder)
    {
        builder.ToTable("BirdSales");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.BuyerName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.BirdType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PricePerBird).HasPrecision(14, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(14, 2);
    }
}

public sealed class BirdHealthEventConfiguration : IEntityTypeConfiguration<BirdHealthEvent>
{
    public void Configure(EntityTypeBuilder<BirdHealthEvent> builder)
    {
        builder.ToTable("BirdHealthEvents");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.BirdType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Cause).HasMaxLength(200);
    }
}

public sealed class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.ToTable("Medications");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.MedicationName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MedicationType).HasMaxLength(80);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}

public sealed class DebeakingScheduleConfiguration : IEntityTypeConfiguration<DebeakingSchedule>
{
    public void Configure(EntityTypeBuilder<DebeakingSchedule> builder)
    {
        builder.ToTable("DebeakingSchedules");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.DebeakingType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}
