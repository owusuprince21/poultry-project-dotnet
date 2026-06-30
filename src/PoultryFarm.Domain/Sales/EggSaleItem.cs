using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Sales;

public sealed class EggSaleItem : AuditableEntity
{
    public Guid EggSaleId { get; set; }
    public EggSale? EggSale { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public EggColor EggColor { get; set; }
    public EggSize Size { get; set; }
    public int Eggs { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; private set; }

    public void RecalculateTotal()
    {
        LineTotal = Eggs * UnitPrice;
    }
}
