using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Feed;

public sealed class FeedConsumption : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public Guid? FeedConfigurationId { get; set; }
    public FeedConfiguration? FeedConfiguration { get; set; }
    public DateOnly Date { get; set; }
    public EggCollectionPeriod CollectionPeriod { get; set; } = EggCollectionPeriod.Morning;
    public decimal AmountKg { get; set; }
    public FeedType FeedType { get; set; }
    public decimal? CostPerKg { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public int? BagSizeKg { get; set; }
    public int BagsUsed { get; set; }
    public decimal TotalCost => AmountKg * (CostPerKg ?? 0m);
}
