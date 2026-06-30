using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Communication;

public sealed class FarmAssistanceMessage : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid CompanyId { get; set; }
    public string Sender { get; set; } = "user";
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
}
