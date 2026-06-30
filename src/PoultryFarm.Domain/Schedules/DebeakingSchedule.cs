using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Schedules;

public sealed class DebeakingSchedule : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public string DebeakingType { get; set; } = string.Empty;
    public DateOnly ScheduledDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public int BirdAgeWeeks { get; set; }
    public string? PerformedBy { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;
    public string? Notes { get; set; }
}
