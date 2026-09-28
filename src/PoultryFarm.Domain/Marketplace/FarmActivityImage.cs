using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Marketplace;

public sealed class FarmActivityImage : AuditableEntity
{
    public Guid PostId { get; set; }
    public FarmActivityPost? Post { get; set; }
    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
