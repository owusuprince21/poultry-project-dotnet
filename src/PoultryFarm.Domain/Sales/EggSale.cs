using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Sales;

public sealed class EggSale : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public DateOnly SaleDate { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string ReceiptId { get; set; } = $"RCPT-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
    public string? Notes { get; set; }
    public decimal GrandTotal { get; private set; }
    public PaymentType PaymentType { get; set; } = PaymentType.Cash;
    public DateOnly? CreditDueDate { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    public DateTimeOffset? PaidAt { get; set; }
    public ICollection<EggSaleItem> Items { get; set; } = [];

    public void RecalculateTotal()
    {
        GrandTotal = Items.Sum(i => i.LineTotal);
        PaymentStatus = PaymentType == PaymentType.Cash ? PaymentStatus.Paid : PaymentStatus.OnCredit;
    }
}
