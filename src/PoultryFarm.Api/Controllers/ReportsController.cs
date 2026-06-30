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

    private async Task<Guid?> CompanyIdAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
        return user?.CompanyId;
    }
}
