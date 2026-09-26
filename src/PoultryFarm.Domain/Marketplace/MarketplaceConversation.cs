using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Marketplace;

public sealed class MarketplaceConversation : AuditableEntity
{
    public Guid ListingId { get; set; }
    public MarketplaceListing? Listing { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BuyerUserId { get; set; }
    public Guid FarmContactUserId { get; set; }
    public Guid? InquiryId { get; set; }
    public MarketplaceInquiry? Inquiry { get; set; }
}
