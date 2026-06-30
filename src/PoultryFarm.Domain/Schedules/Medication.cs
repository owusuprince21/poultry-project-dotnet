using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Schedules;

public sealed class Medication : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string MedicationType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public DateOnly ScheduledDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public DateOnly? NextDueDate { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public string? AdministeredBy { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Scheduled;
    public string? Notes { get; set; }
}
