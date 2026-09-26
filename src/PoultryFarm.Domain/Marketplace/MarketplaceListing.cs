using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Marketplace;

public sealed class MarketplaceListing : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public MarketplaceListingType ListingType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? QuantityOffered { get; set; }
    public string UnitLabel { get; set; } = "units";
    public decimal? PriceAmount { get; set; }
    public string? PriceText { get; set; }
    public string? ImageUrl { get; set; }
    public MarketplaceListingStatus Status { get; set; } = MarketplaceListingStatus.Draft;
    public Guid? BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid? BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
