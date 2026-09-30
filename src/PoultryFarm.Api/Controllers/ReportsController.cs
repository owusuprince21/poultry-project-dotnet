using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Domain.Common;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string operation = "production", [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var start = from ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        var end = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        object data = operation.ToLowerInvariant() switch
        {
            "feed" => await dbContext.FeedConsumptions.AsNoTracking().Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end).GroupBy(x => x.Date).Select(g => new { date = g.Key, total = g.Sum(x => x.AmountKg) }).OrderBy(x => x.date).ToListAsync(cancellationToken),
            "health" => await dbContext.BirdHealthEvents.AsNoTracking().Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end).GroupBy(x => new { x.Date, x.Status }).Select(g => new { date = g.Key.Date, status = g.Key.Status, total = g.Sum(x => x.Count) }).OrderBy(x => x.date).ToListAsync(cancellationToken),
            "sales" => await dbContext.EggSales.AsNoTracking().Where(x => x.CompanyId == companyId && x.SaleDate >= start && x.SaleDate <= end).GroupBy(x => x.SaleDate).Select(g => new { date = g.Key, total = g.Sum(x => x.GrandTotal) }).OrderBy(x => x.date).ToListAsync(cancellationToken),
            _ => await dbContext.EggProductions.AsNoTracking().Where(x => x.CompanyId == companyId && x.Date >= start && x.Date <= end).GroupBy(x => x.Date).Select(g => new { date = g.Key, total = g.Sum(x => x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs) }).OrderBy(x => x.date).ToListAsync(cancellationToken)
        };
        return Ok(new { operation, from = start, to = end, data });
    }

    [HttpGet("daily")]
    public async Task<ActionResult<DailySummaryDto>> Daily([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue)
        {
            return Forbid();
        }

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var eggs = await dbContext.EggProductions
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Where(x => x.CompanyId == companyId && x.Date == day)
            .ToListAsync(cancellationToken);
        eggs = eggs.OrderBy(x => x.CollectionPeriod).ThenBy(x => x.Batch?.BatchNumber).ToList();

        var feed = await dbContext.FeedConsumptions
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.FeedConfiguration)
            .Where(x => x.CompanyId == companyId && x.Date == day)
            .ToListAsync(cancellationToken);
        feed = feed.OrderBy(x => x.CollectionPeriod).ThenBy(x => x.Batch?.BatchNumber).ToList();

        var health = await dbContext.BirdHealthEvents
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Where(x => x.CompanyId == companyId && x.Date == day)
            .OrderBy(x => x.Batch!.BatchNumber)
            .ToListAsync(cancellationToken);

        var eggSales = await dbContext.EggSales
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.CompanyId == companyId && x.SaleDate == day)
            .OrderBy(x => x.BuyerName)
            .ToListAsync(cancellationToken);

        var birdSales = await dbContext.BirdSales
            .AsNoTracking()
            .Include(x => x.Batch)
            .Where(x => x.CompanyId == companyId && x.SaleDate == day)
            .OrderBy(x => x.BuyerName)
            .ToListAsync(cancellationToken);

        int EggsFor(EggCollectionPeriod period) => eggs
            .Where(x => x.CollectionPeriod == period)
            .Sum(x => x.TotalEggs);

        decimal FeedFor(EggCollectionPeriod period) => feed
            .Where(x => x.CollectionPeriod == period)
            .Sum(x => x.AmountKg);

        var sales = eggSales
            .Select(x => new DailySaleLineDto(
                "Eggs",
                x.BuyerName,
                $"{x.Items.Sum(item => item.Eggs):N0} eggs",
                x.GrandTotal))
            .Concat(birdSales.Select(x => new DailySaleLineDto(
                "Birds",
                x.BuyerName,
                $"{x.BirdsSold:N0} birds from {x.Batch?.BatchNumber ?? "batch"}",
                x.TotalAmount)))
            .ToList();

        return Ok(new DailySummaryDto(
            day,
            eggs.Sum(x => x.TotalEggs),
            EggsFor(EggCollectionPeriod.Morning),
            EggsFor(EggCollectionPeriod.Afternoon),
            EggsFor(EggCollectionPeriod.Evening),
            feed.Sum(x => x.AmountKg),
            FeedFor(EggCollectionPeriod.Morning),
            FeedFor(EggCollectionPeriod.Afternoon),
            FeedFor(EggCollectionPeriod.Evening),
            health.Where(x => x.Status == BirdHealthStatus.Sick).Sum(x => x.Count),
            health.Where(x => x.Status == BirdHealthStatus.Dead).Sum(x => x.Count),
            eggSales.Sum(x => x.GrandTotal),
            birdSales.Sum(x => x.TotalAmount),
            eggs.Select(x => new DailyEggLineDto(
                x.CollectionPeriod.ToString(),
                x.Batch?.BatchNumber ?? string.Empty,
                (x.BatchVariant?.Color ?? VariantColor.Mixed).ToString(),
                x.CollectionType.ToString(),
                x.TotalEggs)).ToList(),
            feed.Select(x => new DailyFeedLineDto(
                x.CollectionPeriod.ToString(),
                x.Batch?.BatchNumber ?? string.Empty,
                x.FeedConfiguration?.Name ?? x.FeedType.ToString(),
                x.BagsUsed,
                x.BagSizeKg ?? 0,
                x.AmountKg)).ToList(),
            health.Select(x => new DailyHealthLineDto(
                x.Batch?.BatchNumber ?? string.Empty,
                (x.BatchVariant?.Color ?? VariantColor.Mixed).ToString(),
                x.Status.ToString(),
                x.Count,
                x.Cause)).ToList(),
            sales));
    }

    private async Task<Guid?> CompanyIdAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
        return user?.CompanyId;
    }
}

public sealed record DailySummaryDto(
    DateOnly Date,
    int TotalEggs,
    int MorningEggs,
    int AfternoonEggs,
    int EveningEggs,
    decimal TotalFeedKg,
    decimal MorningFeedKg,
    decimal AfternoonFeedKg,
    decimal EveningFeedKg,
    int SickBirds,
    int DeadBirds,
    decimal EggSalesTotal,
    decimal BirdSalesTotal,
    IReadOnlyList<DailyEggLineDto> Eggs,
    IReadOnlyList<DailyFeedLineDto> Feed,
    IReadOnlyList<DailyHealthLineDto> Health,
    IReadOnlyList<DailySaleLineDto> Sales);

public sealed record DailyEggLineDto(string Period, string BatchNumber, string BirdColor, string CollectionType, int Eggs);

public sealed record DailyFeedLineDto(string Period, string BatchNumber, string FeedType, int BagsUsed, int BagSizeKg, decimal TotalKg);

public sealed record DailyHealthLineDto(string BatchNumber, string BirdColor, string Status, int Count, string? Cause);

public sealed record DailySaleLineDto(string Kind, string Buyer, string Detail, decimal Amount);
