using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Feed;

public sealed class FeedConfiguration : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BagSizeKg { get; set; }
    public bool IsActive { get; set; } = true;
}
