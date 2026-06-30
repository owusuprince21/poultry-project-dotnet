using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Feed;

public sealed class FeedStock : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public FeedType FeedType { get; set; }
    public decimal CurrentStockKg { get; set; }
    public decimal MinimumThresholdKg { get; set; } = 100m;
    public DateOnly? LastRestockedDate { get; set; }
    public string? Supplier { get; set; }
    public decimal CostPerKg { get; set; }
    public bool IsLowStock => CurrentStockKg <= MinimumThresholdKg;
}
