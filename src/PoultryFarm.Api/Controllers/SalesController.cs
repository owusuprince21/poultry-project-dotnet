using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Sales;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/sales")]
[Authorize]
public sealed class SalesController(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager, IActivityNotifier notifier) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var produced = await dbContext.EggProductions.Where(x => x.CompanyId == companyId).GroupBy(_ => 1).Select(g => new
        {
            small = g.Sum(x => x.SmallEggs), medium = g.Sum(x => x.MediumEggs), large = g.Sum(x => x.LargeEggs), jumbo = g.Sum(x => x.ExtraLargeEggs), unsorted = g.Sum(x => x.UnsortedEggs)
        }).FirstOrDefaultAsync(cancellationToken);
        var sold = await dbContext.EggSaleItems.Where(x => x.EggSale != null && x.EggSale.CompanyId == companyId).GroupBy(x => x.Size).Select(g => new { size = g.Key, eggs = g.Sum(x => x.Eggs) }).ToListAsync(cancellationToken);
        int Stock(EggSize size, int total) => Math.Max(0, total - sold.Where(x => x.size == size).Sum(x => x.eggs));
        return Ok(new
        {
            small = Stock(EggSize.Small, produced?.small ?? 0),
            medium = Stock(EggSize.Medium, produced?.medium ?? 0),
            large = Stock(EggSize.Large, produced?.large ?? 0),
            jumbo = Stock(EggSize.ExtraLarge, produced?.jumbo ?? 0),
            unsorted = Stock(EggSize.Unsorted, produced?.unsorted ?? 0)
        });
    }

    [HttpGet("customers")]
    public async Task<IActionResult> Customers(CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var customers = await dbContext.EggSales.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.BuyerName)
            .Concat(dbContext.BirdSales.AsNoTracking().Where(x => x.CompanyId == companyId).Select(x => x.BuyerName))
            .Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        return Ok(customers);
    }

    [HttpGet("leaderboard")]
    public async Task<IActionResult> Leaderboard(CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var egg = await dbContext.EggSales.AsNoTracking().Where(x => x.CompanyId == companyId).GroupBy(x => x.BuyerName).Select(g => new { customer = g.Key, revenue = g.Sum(x => x.GrandTotal), purchases = g.Count() }).ToListAsync(cancellationToken);
        var bird = await dbContext.BirdSales.AsNoTracking().Where(x => x.CompanyId == companyId).GroupBy(x => x.BuyerName).Select(g => new { customer = g.Key, revenue = g.Sum(x => x.TotalAmount), purchases = g.Count() }).ToListAsync(cancellationToken);
        return Ok(egg.Concat(bird).GroupBy(x => x.customer).Select(g => new { customer = g.Key, revenue = g.Sum(x => x.revenue), purchases = g.Sum(x => x.purchases) }).OrderByDescending(x => x.revenue).Take(5));
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var eggs = await dbContext.EggSales.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.SaleDate).Take(50).Select(x => new { x.Id, type = "Egg", date = x.SaleDate, customer = x.BuyerName, amount = x.GrandTotal, receipt = x.ReceiptId }).ToListAsync(cancellationToken);
        var birds = await dbContext.BirdSales.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.SaleDate).Take(50).Select(x => new { x.Id, type = "Bird", date = x.SaleDate, customer = x.BuyerName, amount = x.TotalAmount, receipt = "" }).ToListAsync(cancellationToken);
        return Ok(eggs.Concat(birds).OrderByDescending(x => x.date).Take(50));
    }

    [HttpGet("eggs/{id:guid}/receipt")]
    public async Task<IActionResult> EggReceipt(Guid id, CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();

        var sale = await dbContext.EggSales
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId.Value, cancellationToken);

        return sale is null ? NotFound(new { detail = "Receipt was not found." }) : Ok(BuildEggReceipt(sale));
    }

    [HttpGet("eggs/{id:guid}/receipt.pdf")]
    public async Task<IActionResult> EggReceiptPdf(Guid id, CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();

        var sale = await dbContext.EggSales
            .AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId.Value, cancellationToken);

        if (sale is null)
        {
            return NotFound(new { detail = "Receipt was not found." });
        }

        var receipt = BuildEggReceipt(sale);
        var officerName = await OfficerNameAsync(sale.CreatedByUserId, sale.Id, sale.CompanyId, cancellationToken);
        var pdf = BuildReceiptPdf(receipt, sale.Company?.Name ?? "Poultry Farm", officerName);
        return File(pdf, "application/pdf", $"{receipt.ReceiptId}.pdf");
    }

    [HttpPost("eggs")]
    public async Task<IActionResult> EggSale(EggSaleRequest request, CancellationToken cancellationToken)
    {
        var user = await UserAsync();
        if (user?.CompanyId is not Guid companyId) return Forbid();
        var saleLines = request.Items.Count > 0
            ? request.Items
            : [new EggSaleLineRequest(request.Size, request.Crates, request.Pieces, request.CostPerCrate)];

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return BadRequest(new { customerName = "Customer name is required." });
        }

        if (saleLines.Count == 0 || saleLines.Any(x => x.Crates < 0 || x.Pieces < 0 || x.Pieces > 29 || x.CostPerCrate <= 0 || x.Crates * 30 + x.Pieces <= 0))
        {
            return BadRequest(new { items = "Add at least one valid egg size with quantity and price." });
        }

        var requestedBySize = saleLines
            .GroupBy(x => x.Size)
            .Select(x => new { size = x.Key, eggs = x.Sum(line => line.Crates * 30 + line.Pieces) })
            .ToArray();
        var availableBySize = await GetAvailableEggsBySizeAsync(companyId, cancellationToken);
        var oversoldLine = requestedBySize.FirstOrDefault(x => x.eggs > availableBySize.GetValueOrDefault(x.size));
        if (oversoldLine is not null)
        {
            var availableEggs = availableBySize.GetValueOrDefault(oversoldLine.size);
            return BadRequest(new
            {
                items = $"Only {FormatEggQuantity(availableEggs)} of {DisplayEggSize(oversoldLine.size).ToLowerInvariant()} eggs are available. Reduce the sale quantity before recording."
            });
        }

        var sale = new EggSale { CompanyId = companyId, SaleDate = request.SaleDate, BuyerName = request.CustomerName.Trim(), PaymentType = PaymentType.Cash, PaymentStatus = PaymentStatus.Paid, CreatedByUserId = user.Id };
        foreach (var line in saleLines)
        {
            var item = new EggSaleItem
            {
                BatchId = request.BatchId,
                BatchVariantId = request.BatchVariantId,
                EggColor = request.EggColor,
                Size = line.Size,
                Eggs = line.Crates * 30 + line.Pieces,
                UnitPrice = line.CostPerCrate / 30m
            };
            item.RecalculateTotal();
            sale.Items.Add(item);
        }

        sale.RecalculateTotal();
        dbContext.EggSales.Add(sale);
        await dbContext.SaveChangesAsync(cancellationToken);
        var receipt = BuildEggReceipt(sale);
        var totalEggs = receipt.Eggs;
        await notifier.NotifyCompanyAsync(companyId, "Egg sale recorded", $"{DisplayName(user)} recorded an egg sale to {sale.BuyerName}: {totalEggs:N0} eggs across {sale.Items.Count:N0} size line(s) for GHS {sale.GrandTotal:N2}. Receipt {sale.ReceiptId}.", user.Id, DisplayName(user), targetType: "sales", targetId: sale.Id, cancellationToken: cancellationToken);
        return Ok(receipt);
    }

    [HttpPost("birds")]
    public async Task<IActionResult> BirdSale(BirdSaleRequest request, CancellationToken cancellationToken)
    {
        var user = await UserAsync();
        if (user?.CompanyId is not Guid companyId) return Forbid();
        var variant = await dbContext.BatchVariants
            .Include(x => x.Batch)
                .ThenInclude(x => x!.Variants)
            .FirstOrDefaultAsync(x => x.Id == request.BatchVariantId && x.Batch != null && x.Batch.CompanyId == companyId, cancellationToken);
        if (variant?.Batch is null) return BadRequest(new { detail = "Select a valid batch color." });
        variant.DecreaseCurrentCount(request.BirdsSold);
        variant.Batch.SyncCountsFromVariants();
        var sale = new BirdSale { CompanyId = companyId, BatchId = variant.BatchId, BatchVariantId = variant.Id, SaleDate = request.SaleDate, BuyerName = request.CustomerName.Trim(), BirdType = variant.Batch.BirdType, BirdsSold = request.BirdsSold, PricePerBird = request.PricePerBird, Notes = request.Notes };
        sale.RecalculateTotal();
        dbContext.BirdSales.Add(sale);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCompanyAsync(companyId, "Bird sale recorded", $"{DisplayName(user)} recorded a bird sale to {sale.BuyerName}: {sale.BirdsSold:N0} birds from batch {variant.Batch.BatchNumber} for GHS {sale.TotalAmount:N2}.", user.Id, DisplayName(user), targetType: "sales", targetId: sale.Id, cancellationToken: cancellationToken);
        return Ok(new { sale.Id, sale.BuyerName, sale.SaleDate, sale.BirdsSold, sale.PricePerBird, sale.TotalAmount });
    }

    private async Task<Guid?> CompanyIdAsync() => (await UserAsync())?.CompanyId;

    private async Task<ApplicationUser?> UserAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
    }

    private static string DisplayName(ApplicationUser user) => string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim()) ? user.UserName ?? "User" : $"{user.FirstName} {user.LastName}".Trim();

    private async Task<string> OfficerNameAsync(Guid? officerId, Guid saleId, Guid companyId, CancellationToken cancellationToken)
    {
        if (officerId.HasValue)
        {
            var user = await dbContext.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == officerId.Value, cancellationToken);

            if (user is not null)
            {
                return DisplayName(user);
            }
        }

        var activityActorId = await dbContext.AppNotifications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TargetType == "sales" && x.TargetId == saleId && x.ActorUserId.HasValue)
            .OrderByDescending(x => x.SentAt)
            .Select(x => x.ActorUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (activityActorId.HasValue)
        {
            var activityUser = await dbContext.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == activityActorId.Value, cancellationToken);

            if (activityUser is not null)
            {
                return DisplayName(activityUser);
            }
        }

        var activityActorName = await dbContext.AppNotifications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TargetType == "sales" && x.TargetId == saleId && x.ActorName != "")
            .OrderByDescending(x => x.SentAt)
            .Select(x => x.ActorName)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(activityActorName) ? "Farm officer" : activityActorName;
    }

    private static EggReceiptResponse BuildEggReceipt(EggSale sale)
    {
        var totalEggs = sale.Items.Sum(x => x.Eggs);
        var lines = sale.Items
            .OrderBy(x => x.Size)
            .Select(x => new EggReceiptLineResponse(
                x.Size,
                x.Eggs,
                x.Eggs / 30,
                x.Eggs % 30,
                x.UnitPrice * 30m,
                x.LineTotal))
            .ToArray();

        return new EggReceiptResponse(
            sale.Id,
            sale.ReceiptId,
            sale.BuyerName,
            sale.SaleDate,
            sale.GrandTotal,
            totalEggs,
            totalEggs / 30,
            totalEggs % 30,
            lines);
    }

    private async Task<Dictionary<EggSize, int>> GetAvailableEggsBySizeAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var produced = await dbContext.EggProductions
            .Where(x => x.CompanyId == companyId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                small = g.Sum(x => x.SmallEggs),
                medium = g.Sum(x => x.MediumEggs),
                large = g.Sum(x => x.LargeEggs),
                jumbo = g.Sum(x => x.ExtraLargeEggs),
                unsorted = g.Sum(x => x.UnsortedEggs)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var sold = await dbContext.EggSaleItems
            .Where(x => x.EggSale != null && x.EggSale.CompanyId == companyId)
            .GroupBy(x => x.Size)
            .Select(g => new { size = g.Key, eggs = g.Sum(x => x.Eggs) })
            .ToListAsync(cancellationToken);

        int Stock(EggSize size, int total) => Math.Max(0, total - sold.Where(x => x.size == size).Sum(x => x.eggs));

        return new Dictionary<EggSize, int>
        {
            [EggSize.Small] = Stock(EggSize.Small, produced?.small ?? 0),
            [EggSize.Medium] = Stock(EggSize.Medium, produced?.medium ?? 0),
            [EggSize.Large] = Stock(EggSize.Large, produced?.large ?? 0),
            [EggSize.ExtraLarge] = Stock(EggSize.ExtraLarge, produced?.jumbo ?? 0),
            [EggSize.Unsorted] = Stock(EggSize.Unsorted, produced?.unsorted ?? 0)
        };
    }

    private static byte[] BuildReceiptPdf(EggReceiptResponse receipt, string companyName, string officerName)
    {
        var content = new StringBuilder();
        content.AppendLine("BT");
        WriteCenteredText(content, companyName, 14, 153, 760);
        WriteCenteredText(content, "EGG SALES RECEIPT", 9, 153, 742);
        WriteText(content, Repeat("-", 42), 7, 24, 726);
        WriteText(content, $"Receipt: {receipt.ReceiptId}", 8, 24, 712);
        WriteText(content, $"Date: {receipt.SaleDate:dd MMM yyyy}", 8, 24, 700);
        WriteText(content, $"Customer: {TrimForReceipt(receipt.BuyerName, 27)}", 8, 24, 688);
        WriteText(content, $"Farm Officer: {TrimForReceipt(officerName, 28)}", 8, 24, 676);
        WriteText(content, Repeat("-", 42), 7, 24, 662);
        WriteText(content, "ITEM              QTY    RATE      AMT", 7, 24, 648);
        WriteText(content, Repeat("-", 42), 7, 24, 638);

        var y = 624;
        foreach (var item in receipt.Items.OrderBy(x => x.Size))
        {
            WriteText(content, FormatReceiptLine(item), 7, 24, y);
            y -= 13;
        }

        y -= 2;
        WriteText(content, Repeat("-", 42), 7, 24, y);
        y -= 15;
        WriteText(content, $"TOTAL EGGS: {receipt.Eggs:N0}", 8, 24, y);
        y -= 13;
        WriteText(content, $"QTY: {receipt.Crates:N0} crates, {receipt.Pieces:N0} pcs", 8, 24, y);
        y -= 16;
        WriteText(content, $"GRAND TOTAL: GHS {receipt.GrandTotal:N2}", 10, 24, y);
        y -= 18;
        WriteText(content, Repeat("-", 42), 7, 24, y);
        y -= 16;
        WriteCenteredText(content, "Thank you for your business", 8, 153, y);
        y -= 12;
        WriteCenteredText(content, "Please keep this receipt", 7, 153, y);
        content.AppendLine("ET");

        return BuildSimplePdf(content.ToString());
    }

    private static byte[] BuildSimplePdf(string pageContent)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 306 792] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(pageContent)} >>\nstream\n{pageContent}\nendstream"
        };

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefPosition = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(stream, $"{offset:0000000000} 00000 n \n");
        }

        WriteAscii(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF");
        return stream.ToArray();
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes);
    }

    private static string EscapePdfText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static void WriteText(StringBuilder content, string text, int fontSize, int x, int y, string font = "F2")
    {
        content.AppendLine($"/{font} {fontSize} Tf");
        content.AppendLine($"1 0 0 1 {x} {y} Tm");
        content.AppendLine($"({EscapePdfText(ToPdfAscii(text))}) Tj");
    }

    private static void WriteCenteredText(StringBuilder content, string text, int fontSize, int centerX, int y)
    {
        var safeText = ToPdfAscii(text);
        var approximateWidth = safeText.Length * fontSize * 0.5m;
        var x = Math.Max(12, centerX - (int)(approximateWidth / 2));
        WriteText(content, safeText, fontSize, x, y, "F1");
    }

    private static string FormatReceiptLine(EggReceiptLineResponse item)
    {
        var name = TrimForReceipt(DisplayEggSize(item.Size), 13).PadRight(13);
        var quantity = $"{item.Crates:N0}c {item.Pieces:N0}p".PadLeft(8);
        var rate = item.CostPerCrate.ToString("N2").PadLeft(8);
        var amount = item.LineTotal.ToString("N2").PadLeft(8);
        return $"{name}{quantity}{rate}{amount}";
    }

    private static string TrimForReceipt(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = ToPdfAscii(value.Trim());
        return trimmed.Length <= length ? trimmed : trimmed[..Math.Max(0, length - 1)] + ".";
    }

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));

    private static string ToPdfAscii(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch <= 127 ? ch : ' ');
        }

        return builder.ToString();
    }

    private static string DisplayEggSize(EggSize size) =>
        size switch
        {
            EggSize.Small => "Small",
            EggSize.Medium => "Medium",
            EggSize.Large => "Large",
            EggSize.ExtraLarge => "Jumbo",
            EggSize.Unsorted => "Unsorted",
            _ => "Eggs"
        };

    private static string FormatEggQuantity(int eggs) => $"{eggs / 30:N0} crates, {eggs % 30:N0} pieces";
}

public sealed class EggSaleRequest
{
    public string CustomerName { get; set; } = string.Empty;
    public Guid BatchId { get; set; }
    public Guid BatchVariantId { get; set; }
    public EggColor EggColor { get; set; }
    public EggSize Size { get; set; }
    public DateOnly SaleDate { get; set; }
    public int Crates { get; set; }
    public int Pieces { get; set; }
    public decimal CostPerCrate { get; set; }
    public IReadOnlyCollection<EggSaleLineRequest> Items { get; set; } = [];
}

public sealed record EggSaleLineRequest(EggSize Size, int Crates, int Pieces, decimal CostPerCrate);
public sealed record EggReceiptResponse(Guid Id, string ReceiptId, string BuyerName, DateOnly SaleDate, decimal GrandTotal, int Eggs, int Crates, int Pieces, IReadOnlyCollection<EggReceiptLineResponse> Items);
public sealed record EggReceiptLineResponse(EggSize Size, int Eggs, int Crates, int Pieces, decimal CostPerCrate, decimal LineTotal);
public sealed record BirdSaleRequest(string CustomerName, Guid BatchId, Guid BatchVariantId, DateOnly SaleDate, int BirdsSold, decimal PricePerBird, string? Notes);
