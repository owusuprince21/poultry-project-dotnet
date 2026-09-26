using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Marketplace;

public sealed class FarmActivityPost : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? MediaUrl { get; set; }
    public bool IsPublished { get; set; } = true;
    public DateTimeOffset? PublishedAt { get; set; }
    public int ShareCount { get; set; }
    public ICollection<FarmActivityComment> Comments { get; set; } = new List<FarmActivityComment>();
    public ICollection<FarmActivityLike> Likes { get; set; } = new List<FarmActivityLike>();
}
