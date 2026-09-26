using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Batches.DTOs;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Infrastructure.Identity;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/batches")]
[Authorize]
public sealed class BatchesController(
    IApplicationDbContext dbContext,
    IActivityNotifier activityNotifier,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<BatchDto>>> Get([FromQuery] Guid? company, CancellationToken cancellationToken)
    {
        var companyScope = ResolveCompanyScope(company);
        if (companyScope.Blocked)
        {
            return Forbid();
        }

        var query = dbContext.Batches
            .AsNoTracking()
            .Include(x => x.Variants)
            .OrderByDescending(x => x.CreatedAt)
            .AsQueryable();

        if (companyScope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyScope.CompanyId.Value);
        }

        var batches = await query.Select(x => ToDto(x)).ToListAsync(cancellationToken);
        return Ok(batches);
    }

    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyCollection<BatchDto>>> Active([FromQuery] Guid? company, CancellationToken cancellationToken)
    {
        var companyScope = ResolveCompanyScope(company);
        if (companyScope.Blocked)
        {
            return Forbid();
        }

        var query = dbContext.Batches
            .AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.Status == Domain.Common.BatchStatus.Active);

        if (companyScope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyScope.CompanyId.Value);
        }

        var batches = await query.OrderByDescending(x => x.CreatedAt).Select(x => ToDto(x)).ToListAsync(cancellationToken);
        return Ok(batches);
    }

    [HttpPost]
    public async Task<ActionResult<BatchDto>> Create(CreateBatchRequest request, CancellationToken cancellationToken)
    {
        var companyScope = ResolveCompanyScope(request.CompanyId);
        if (companyScope.Blocked || !companyScope.CompanyId.HasValue)
        {
            return Forbid();
        }

        var batch = new Batch
        {
            CompanyId = companyScope.CompanyId.Value,
            BirdType = request.BirdType,
            ArrivalDate = request.ArrivalDate,
            Breed = request.Breed.Trim(),
            Supplier = request.Supplier,
            ExpectedSaleDate = request.ExpectedSaleDate,
            Notes = request.Notes,
            BatchNumber = await GenerateBatchNumber(companyScope.CompanyId.Value, request.ArrivalDate, cancellationToken)
        };

        foreach (var variantRequest in request.Variants)
        {
            var variant = new BatchVariant
            {
                Color = variantRequest.Color,
                InitialCount = variantRequest.InitialCount,
                Notes = variantRequest.Notes
            };
            variant.SetCurrentCount(variantRequest.InitialCount);
            batch.Variants.Add(variant);
        }

        batch.SyncCountsFromVariants();
        dbContext.Batches.Add(batch);
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = await GetActorAsync();
        var colorSummary = string.Join(", ", batch.Variants.Select(v => $"{v.Color}: {v.InitialCount:N0}"));
        await activityNotifier.NotifyCompanyAsync(
            batch.CompanyId,
            "Batch created",
            $"{actor.Name} created batch {batch.BatchNumber} ({batch.BirdType}, {batch.Breed}) with {batch.InitialCount:N0} birds{(string.IsNullOrWhiteSpace(colorSummary) ? "" : $" — {colorSummary}")}.",
            actor.Id,
            actor.Name,
            targetType: "batch",
            targetId: batch.Id,
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = batch.Id }, ToDto(batch));
    }

    [HttpGet("egg-sale-options")]
    public async Task<IActionResult> EggSaleOptions([FromQuery] Guid? company, CancellationToken cancellationToken)
    {
        var companyScope = ResolveCompanyScope(company);
        if (companyScope.Blocked)
        {
            return Forbid();
        }

        var query = dbContext.Batches
            .AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.BirdType == BirdType.Layer && x.Status == BatchStatus.Active);

        if (companyScope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyScope.CompanyId.Value);
        }

        var batches = await query.ToListAsync(cancellationToken);
        var payload = new List<object>();

        foreach (var batch in batches)
        {
            var variants = new List<object>();
            foreach (var variant in batch.Variants)
            {
                var stock = await EggStockForVariant(variant.Id, cancellationToken);
                if (stock.TotalEggs <= 0)
                {
                    continue;
                }

                var eggRevenue = await dbContext.EggSaleItems
                    .Where(x => x.BatchVariantId == variant.Id)
                    .SumAsync(x => x.LineTotal, cancellationToken);

                variants.Add(new
                {
                    variant_id = variant.Id,
                    color = variant.Color.ToString().ToLowerInvariant(),
                    egg_color = variant.EggColor.ToString().ToLowerInvariant(),
                    available_small = stock.Small,
                    available_medium = stock.Medium,
                    available_large = stock.Large,
                    available_extra_large = stock.ExtraLarge,
                    available_unsorted = stock.Unsorted,
                    total_available_eggs = stock.TotalEggs,
                    egg_revenue = eggRevenue
                });
            }

            if (variants.Count > 0)
            {
                payload.Add(new
                {
                    batch_id = batch.Id,
                    batch_number = batch.BatchNumber,
                    bird_type = batch.BirdType.ToString().ToLowerInvariant(),
                    status = batch.Status.ToString().ToLowerInvariant(),
                    variants
                });
            }
        }

        return Ok(payload);
    }

    [HttpGet("bird-sale-options")]
    public async Task<IActionResult> BirdSaleOptions([FromQuery] Guid? company, CancellationToken cancellationToken)
    {
        var companyScope = ResolveCompanyScope(company);
        if (companyScope.Blocked)
        {
            return Forbid();
        }

        var query = dbContext.Batches
            .AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.Status == BatchStatus.Active || x.Status == BatchStatus.Sold);

        if (companyScope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyScope.CompanyId.Value);
        }

        var batches = await query.ToListAsync(cancellationToken);
        return Ok(batches.Select(batch => new
        {
            batch_id = batch.Id,
            batch_number = batch.BatchNumber,
            bird_type = batch.BirdType.ToString().ToLowerInvariant(),
            status = batch.Status.ToString().ToLowerInvariant(),
            variants = batch.Variants
                .Where(v => v.CurrentCount > 0)
                .Select(v => new
                {
                    variant_id = v.Id,
                    color = v.Color.ToString().ToLowerInvariant(),
                    available_birds = v.CurrentCount
                })
                .ToList()
        }).Where(x => x.variants.Count > 0));
    }

    [HttpPost("{id:guid}/mark-sold")]
    public async Task<IActionResult> MarkSold(Guid id, CancellationToken cancellationToken)
    {
        var batch = await dbContext.Batches
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (batch is null)
        {
            return NotFound(new { detail = "Batch was not found." });
        }

        var companyScope = ResolveCompanyScope(batch.CompanyId);
        if (companyScope.Blocked)
        {
            return Forbid();
        }

        batch.Status = BatchStatus.Sold;
        await dbContext.SaveChangesAsync(cancellationToken);

        var actor = await GetActorAsync();
        await activityNotifier.NotifyCompanyAsync(
            batch.CompanyId,
            "Batch marked for sale",
            $"{actor.Name} marked batch {batch.BatchNumber} ({batch.Breed}) as ready for bird sales.",
            actor.Id,
            actor.Name,
            targetType: "batch",
            targetId: batch.Id,
            cancellationToken: cancellationToken);

        return Ok(ToDto(batch));
    }

    [HttpGet("{id:guid}/revenue")]
    public async Task<IActionResult> Revenue(Guid id, CancellationToken cancellationToken)
    {
        var batch = await dbContext.Batches
            .AsNoTracking()
            .Include(x => x.Variants)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (batch is null)
        {
            return NotFound();
        }

        var eggRevenue = await dbContext.EggSaleItems.Where(x => x.BatchId == id).SumAsync(x => x.LineTotal, cancellationToken);
        var birdRevenue = await dbContext.BirdSales.Where(x => x.BatchId == id).SumAsync(x => x.TotalAmount, cancellationToken);
        var birdsSold = await dbContext.BirdSales.Where(x => x.BatchId == id).SumAsync(x => x.BirdsSold, cancellationToken);

        return Ok(new
        {
            batch_id = batch.Id,
            batch_number = batch.BatchNumber,
            bird_type = batch.BirdType.ToString().ToLowerInvariant(),
            initial_count = batch.InitialCount,
            current_count = batch.CurrentCount,
            birds_sold = birdsSold,
            egg_revenue = eggRevenue,
            bird_sales_revenue = birdRevenue,
            total_revenue = eggRevenue + birdRevenue,
            variants = batch.Variants.Select(v => new
            {
                variant_id = v.Id,
                color = v.Color.ToString().ToLowerInvariant(),
                initial_count = v.InitialCount,
                current_count = v.CurrentCount
            })
        });
    }

    private async Task<(Guid? Id, string Name)> GetActorAsync()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return (null, "A team member");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (userId, "A team member");
        }

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        var name = string.IsNullOrWhiteSpace(fullName) ? user.UserName ?? "A team member" : fullName;
        return (user.Id, name);
    }

    private async Task<string> GenerateBatchNumber(Guid companyId, DateOnly arrivalDate, CancellationToken cancellationToken)
    {
        var prefix = arrivalDate.ToString("yyMM");
        var count = await dbContext.Batches.CountAsync(
            x => x.CompanyId == companyId && x.BatchNumber.StartsWith(prefix),
            cancellationToken);

        return $"{prefix}-{count + 1:000}";
    }

    private CompanyScope ResolveCompanyScope(Guid? requestedCompany)
    {
        if (PermissionHelpers.IsSystemAdmin(User))
        {
            return new CompanyScope(requestedCompany, false);
        }

        var companyClaim = PermissionHelpers.GetCompanyId(User);
        if (!Guid.TryParse(companyClaim, out var currentCompanyId))
        {
            return new CompanyScope(null, true);
        }

        if (requestedCompany.HasValue && requestedCompany.Value != currentCompanyId)
        {
            return new CompanyScope(null, true);
        }

        return new CompanyScope(currentCompanyId, false);
    }

    private static BatchDto ToDto(Batch batch)
    {
        return new BatchDto(
            batch.Id,
            batch.CompanyId,
            batch.BatchNumber,
            batch.BirdType,
            batch.ArrivalDate,
            batch.Breed,
            batch.Supplier,
            batch.ExpectedSaleDate,
            batch.Status,
            batch.InitialCount,
            batch.CurrentCount,
            batch.SurvivalRate,
            batch.Variants.Select(v => new BatchVariantDto(v.Id, v.Color, v.EggColor, v.InitialCount, v.CurrentCount, v.Notes)).ToList());
    }

    private async Task<EggStock> EggStockForVariant(Guid variantId, CancellationToken cancellationToken)
    {
        var produced = await dbContext.EggProductions
            .Where(x => x.BatchVariantId == variantId)
            .GroupBy(_ => 1)
            .Select(g => new EggStock(
                g.Sum(x => x.SmallEggs),
                g.Sum(x => x.MediumEggs),
                g.Sum(x => x.LargeEggs),
                g.Sum(x => x.ExtraLargeEggs),
                g.Sum(x => x.UnsortedEggs)))
            .FirstOrDefaultAsync(cancellationToken) ?? new EggStock(0, 0, 0, 0, 0);

        var sold = await dbContext.EggSaleItems
            .Where(x => x.BatchVariantId == variantId)
            .GroupBy(x => x.Size)
            .Select(g => new { Size = g.Key, Eggs = g.Sum(x => x.Eggs) })
            .ToListAsync(cancellationToken);

        foreach (var item in sold)
        {
            produced = produced.Subtract(item.Size, item.Eggs);
        }

        return produced;
    }

    private sealed record EggStock(int Small, int Medium, int Large, int ExtraLarge, int Unsorted)
    {
        public int TotalEggs => Small + Medium + Large + ExtraLarge + Unsorted;

        public EggStock Subtract(EggSize size, int count)
        {
            return size switch
            {
                EggSize.Small => this with { Small = Math.Max(0, Small - count) },
                EggSize.Medium => this with { Medium = Math.Max(0, Medium - count) },
                EggSize.Large => this with { Large = Math.Max(0, Large - count) },
                EggSize.ExtraLarge => this with { ExtraLarge = Math.Max(0, ExtraLarge - count) },
                EggSize.Unsorted => this with { Unsorted = Math.Max(0, Unsorted - count) },
                _ => this
            };
        }
    }

    private sealed record CompanyScope(Guid? CompanyId, bool Blocked);
}
