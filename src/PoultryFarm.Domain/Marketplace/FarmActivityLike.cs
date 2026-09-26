using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Marketplace;

public sealed class FarmActivityLike : AuditableEntity
{
    public Guid PostId { get; set; }
    public FarmActivityPost? Post { get; set; }
    public Guid? UserId { get; set; }
    public string ReactorKey { get; set; } = string.Empty;
}
