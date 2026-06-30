using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Communication;

public sealed class AppNotification : AuditableEntity
{
    public Guid RecipientUserId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Kind { get; set; } = "activity";
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
