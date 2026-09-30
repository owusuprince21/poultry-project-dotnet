using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Operations;

public sealed class DailyObservation : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public DateOnly Date { get; set; }
    public DailyObservationCategory Category { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? Recommendation { get; set; }
    public string AuthorName { get; set; } = string.Empty;
}

public enum DailyObservationCategory
{
    Eggs = 0,
    Feed = 1,
    Birds = 2,
    General = 3
}
