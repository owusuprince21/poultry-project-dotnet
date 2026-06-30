using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Sales;

public sealed class BirdSale : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public DateOnly SaleDate { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public BirdType BirdType { get; set; }
    public int BirdsSold { get; set; }
    public decimal PricePerBird { get; set; }
    public decimal TotalAmount { get; private set; }
    public string? Notes { get; set; }

    public void RecalculateTotal()
    {
        TotalAmount = BirdsSold * PricePerBird;
    }
}
