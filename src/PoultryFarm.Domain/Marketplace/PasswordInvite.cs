using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Marketplace;

public sealed class PasswordInvite : AuditableEntity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
