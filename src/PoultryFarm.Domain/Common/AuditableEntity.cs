namespace PoultryFarm.Domain.Common;

public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
