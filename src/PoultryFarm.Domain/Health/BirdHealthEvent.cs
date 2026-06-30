using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Health;

public sealed class BirdHealthEvent : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public DateOnly Date { get; set; }
    public BirdType BirdType { get; set; }
    public BirdHealthStatus Status { get; set; }
    public int Count { get; set; }
    public string? Cause { get; set; }
}
