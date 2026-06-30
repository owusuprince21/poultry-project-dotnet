using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Audit;

public sealed class AuditLog : AuditableEntity
{
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string ActorUsername { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string? TargetUsername { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
