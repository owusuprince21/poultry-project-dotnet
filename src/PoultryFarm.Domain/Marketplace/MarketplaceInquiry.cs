using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Marketplace;

public sealed class MarketplaceInquiry : AuditableEntity
{
    public Guid ListingId { get; set; }
    public MarketplaceListing? Listing { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string? BuyerEmail { get; set; }
    public string? BuyerPhone { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal? QuantityRequested { get; set; }
    public MarketplaceInquiryStatus Status { get; set; } = MarketplaceInquiryStatus.New;
    public string? FarmResponseNotes { get; set; }
}
