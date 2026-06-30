using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Batches;

public sealed class Batch : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public BirdType BirdType { get; set; } = BirdType.Layer;
    public DateOnly ArrivalDate { get; set; }
    public string Breed { get; set; } = string.Empty;
    public string? Supplier { get; set; }
    public DateOnly ExpectedSaleDate { get; set; }
    public BatchStatus Status { get; set; } = BatchStatus.Active;
    public string? Notes { get; set; }
    public int InitialCount { get; private set; }
    public int CurrentCount { get; private set; }
    public ICollection<BatchVariant> Variants { get; set; } = [];

    public int AgeInWeeks(DateOnly today) => Math.Max(0, today.DayNumber - ArrivalDate.DayNumber) / 7;

    public decimal SurvivalRate => InitialCount <= 0 ? 0 : Math.Round((decimal)CurrentCount / InitialCount * 100m, 2);

    public void SyncCountsFromVariants()
    {
        InitialCount = Variants.Sum(v => v.InitialCount);
        CurrentCount = Variants.Sum(v => v.CurrentCount);
    }
}
