using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Feed;

public sealed class FeedStockLot : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid? FeedConfigurationId { get; set; }
    public FeedConfiguration? FeedConfiguration { get; set; }
    public FeedType FeedType { get; set; }
    public DateOnly DateArrived { get; set; }
    public string Supplier { get; set; } = "Unknown";
    public int BagSizeKg { get; set; }
    public int BagsIn { get; set; }
    public int BagsRemaining { get; private set; }
    public decimal CostPerBag { get; set; }
    public string? Condition { get; set; }

    public int TotalKgRemaining => BagsRemaining * BagSizeKg;

    public void Receive()
    {
        BagsRemaining = BagsIn;
    }

    public void Consume(int bags)
    {
        if (bags < 0 || bags > BagsRemaining)
        {
            throw new InvalidOperationException("Requested feed bags exceed available stock.");
        }

        BagsRemaining -= bags;
    }

    public void Restore(int bags)
    {
        if (bags < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bags), "Bags cannot be negative.");
        }

        BagsRemaining = Math.Min(BagsIn, BagsRemaining + bags);
    }
}
