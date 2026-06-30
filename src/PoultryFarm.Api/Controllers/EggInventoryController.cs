using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/egg-inventory")]
public sealed class EggInventoryController(IApplicationDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [HttpGet("current")]
    public async Task<IActionResult> Get([FromQuery] Guid? company, [FromQuery] Guid? batch, CancellationToken cancellationToken)
    {
        var query = dbContext.BatchVariants
            .AsNoTracking()
            .Include(x => x.Batch)
            .Where(x => x.Batch != null && x.Batch.BirdType == BirdType.Layer);

        if (company.HasValue)
        {
            query = query.Where(x => x.Batch!.CompanyId == company.Value);
        }

        if (batch.HasValue)
        {
            query = query.Where(x => x.BatchId == batch.Value);
        }

        var variants = await query.ToListAsync(cancellationToken);
        var rows = new List<object>();

        foreach (var variant in variants)
        {
            var produced = await dbContext.EggProductions
                .Where(x => x.BatchVariantId == variant.Id)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    small = g.Sum(x => x.SmallEggs),
                    medium = g.Sum(x => x.MediumEggs),
                    large = g.Sum(x => x.LargeEggs),
                    extra_large = g.Sum(x => x.ExtraLargeEggs),
                    unsorted = g.Sum(x => x.UnsortedEggs)
                })
                .FirstOrDefaultAsync(cancellationToken);

            var sold = await dbContext.EggSaleItems
                .Where(x => x.BatchVariantId == variant.Id)
                .GroupBy(x => x.Size)
                .Select(g => new { size = g.Key, total = g.Sum(x => x.Eggs) })
                .ToListAsync(cancellationToken);

            var small = Math.Max(0, (produced?.small ?? 0) - sold.Where(x => x.size == EggSize.Small).Sum(x => x.total));
            var medium = Math.Max(0, (produced?.medium ?? 0) - sold.Where(x => x.size == EggSize.Medium).Sum(x => x.total));
            var large = Math.Max(0, (produced?.large ?? 0) - sold.Where(x => x.size == EggSize.Large).Sum(x => x.total));
            var extraLarge = Math.Max(0, (produced?.extra_large ?? 0) - sold.Where(x => x.size == EggSize.ExtraLarge).Sum(x => x.total));
            var unsorted = Math.Max(0, (produced?.unsorted ?? 0) - sold.Where(x => x.size == EggSize.Unsorted).Sum(x => x.total));
            var revenue = await dbContext.EggSaleItems.Where(x => x.BatchVariantId == variant.Id).SumAsync(x => x.LineTotal, cancellationToken);

            rows.Add(new
            {
                batch_id = variant.BatchId,
                batch_number = variant.Batch!.BatchNumber,
                variant_id = variant.Id,
                variant_color = variant.Color.ToString().ToLowerInvariant(),
                egg_color = variant.EggColor.ToString().ToLowerInvariant(),
                small,
                medium,
                large,
                extra_large = extraLarge,
                unsorted,
                total_eggs = small + medium + large + extraLarge + unsorted,
                egg_revenue = revenue
            });
        }

        return Ok(rows);
    }
}
