using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Feed;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/feed")]
[Authorize]
public sealed class FeedController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IActivityNotifier activityNotifier) : ControllerBase
{
    [HttpGet("options")]
    public async Task<ActionResult<FeedOptionsDto>> Options(CancellationToken cancellationToken)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return Forbid();
        }

        if (scope.CompanyId.HasValue)
        {
            await EnsureDefaultFeedConfigurationsAsync(scope.CompanyId.Value, cancellationToken);
        }

        var companyId = scope.CompanyId;
        var configurations = await dbContext.FeedConfigurations
            .AsNoTracking()
            .Where(x => !companyId.HasValue || x.CompanyId == companyId.Value)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.BagSizeKg)
            .Select(x => new FeedConfigurationDto(x.Id, x.CompanyId, x.Name, x.BagSizeKg, x.IsActive))
            .ToListAsync(cancellationToken);

        var feedTypes = Enum.GetValues<FeedType>()
            .Select(feedType => new FeedTypeOptionDto(
                feedType,
                DisplayFeedType(feedType),
                configurations
                    .Where(x => LegacyFeedTypeFromName(x.Name) == feedType)
                    .Select(x => x.BagSizeKg)
                    .DefaultIfEmpty(50)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList()))
            .ToList();

        return Ok(new FeedOptionsDto(feedTypes, configurations));
    }

    [HttpPost("configurations")]
    public async Task<ActionResult<FeedConfigurationDto>> CreateConfiguration(FeedConfigurationRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked || !scope.CompanyId.HasValue)
        {
            return Forbid();
        }

        if (!PermissionHelpers.IsCompanyAdmin(User))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.BagSizeKg <= 0)
        {
            return BadRequest(new { detail = "Feed name and bag size are required." });
        }

        var config = new FeedConfiguration
        {
            CompanyId = scope.CompanyId.Value,
            Name = request.Name.Trim(),
            BagSizeKg = request.BagSizeKg,
            IsActive = request.IsActive,
            CreatedByUserId = user.Id
        };

        dbContext.FeedConfigurations.Add(config);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToConfigDto(config));
    }

    [HttpPut("configurations/{id:guid}")]
    public async Task<ActionResult<FeedConfigurationDto>> UpdateConfiguration(Guid id, FeedConfigurationRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!PermissionHelpers.IsCompanyAdmin(User))
        {
            return Forbid();
        }

        var config = await dbContext.FeedConfigurations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (config is null)
        {
            return NotFound(new { detail = "Feed configuration was not found." });
        }

        if (!await CanAccessCompanyAsync(config.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        config.Name = request.Name.Trim();
        config.BagSizeKg = request.BagSizeKg;
        config.IsActive = request.IsActive;
        config.UpdatedByUserId = user.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToConfigDto(config));
    }

    [HttpDelete("configurations/{id:guid}")]
    public async Task<IActionResult> DeleteConfiguration(Guid id, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsCompanyAdmin(User))
        {
            return Forbid();
        }

        var config = await dbContext.FeedConfigurations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (config is null)
        {
            return NotFound(new { detail = "Feed configuration was not found." });
        }

        if (!await CanAccessCompanyAsync(config.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        config.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<FeedMetricsDto>> Metrics([FromQuery] Guid? batchId, CancellationToken cancellationToken)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return Forbid();
        }

        var stockLots = dbContext.FeedStockLots
            .AsNoTracking()
            .Where(x => !scope.CompanyId.HasValue || x.CompanyId == scope.CompanyId.Value);

        var consumptions = dbContext.FeedConsumptions
            .AsNoTracking()
            .Where(x => !scope.CompanyId.HasValue || x.CompanyId == scope.CompanyId.Value);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart = today.AddDays(-6);
        var totalBags = await stockLots.SumAsync(x => x.BagsRemaining, cancellationToken);
        var totalKg = await stockLots.SumAsync(x => (decimal)x.BagsRemaining * x.BagSizeKg, cancellationToken);
        var weeklyUsage = await consumptions
            .Where(x => x.Date >= weekStart && x.Date <= today)
            .SumAsync(x => x.AmountKg, cancellationToken);
        var todayQuery = consumptions.Where(x => x.Date == today);
        if (batchId.HasValue)
        {
            todayQuery = todayQuery.Where(x => x.BatchId == batchId.Value);
        }

        var todayUsage = await todayQuery.SumAsync(x => x.AmountKg, cancellationToken);

        return Ok(new FeedMetricsDto(totalBags, totalKg, Math.Round(weeklyUsage / 7m, 2), todayUsage));
    }

    [HttpGet("consumptions")]
    public async Task<ActionResult<PagedFeedConsumptionResponse>> Consumptions([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return Forbid();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = dbContext.FeedConsumptions
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Include(x => x.FeedConfiguration)
            .Where(x => !scope.CompanyId.HasValue || x.CompanyId == scope.CompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToConsumptionDto(x))
            .ToListAsync(cancellationToken);

        return Ok(new PagedFeedConsumptionResponse(items, total, page, pageSize));
    }

    [HttpGet("stocks")]
    public async Task<ActionResult<PagedFeedStockLotResponse>> StockLots([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return Forbid();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = dbContext.FeedStockLots
            .AsNoTracking()
            .Include(x => x.FeedConfiguration)
            .Where(x => !scope.CompanyId.HasValue || x.CompanyId == scope.CompanyId.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.DateArrived)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToStockLotDto(x))
            .ToListAsync(cancellationToken);

        return Ok(new PagedFeedStockLotResponse(items, total, page, pageSize));
    }

    [HttpPost("consumptions")]
    public async Task<ActionResult<FeedConsumptionDto>> CreateConsumption(FeedConsumptionRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var variant = await GetScopedVariantAsync(request.BatchVariantId, cancellationToken);
        if (variant is null)
        {
            return BadRequest(new { detail = "Select a valid batch color for your company." });
        }

        var feedConfig = await GetScopedFeedConfigurationAsync(request.FeedConfigurationId, cancellationToken);
        if (feedConfig is null)
        {
            return BadRequest(new { detail = "Select a valid feed type configuration." });
        }

        var totalKg = request.BagsUsed * feedConfig.BagSizeKg;
        if (request.BagsUsed <= 0)
        {
            return BadRequest(new { detail = "Bags used must be greater than zero." });
        }

        if (!await ConsumeStockAsync(variant.Batch!.CompanyId, feedConfig.Id, LegacyFeedTypeFromName(feedConfig.Name), feedConfig.BagSizeKg, request.BagsUsed, cancellationToken))
        {
            return BadRequest(new { detail = "Insufficient feed stock for the selected feed type and bag size." });
        }

        var consumption = new FeedConsumption
        {
            CompanyId = variant.Batch.CompanyId,
            BatchId = variant.BatchId,
            BatchVariantId = variant.Id,
            Date = request.Date,
            FeedConfigurationId = feedConfig.Id,
            FeedType = LegacyFeedTypeFromName(feedConfig.Name),
            BagSizeKg = feedConfig.BagSizeKg,
            BagsUsed = request.BagsUsed,
            AmountKg = totalKg,
            Notes = request.Notes,
            CreatedByUserId = user.Id
        };

        dbContext.FeedConsumptions.Add(consumption);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, consumption.CompanyId, "Feed usage recorded", $"{DisplayName(user)} recorded {consumption.AmountKg:N1} kg feed usage for batch {variant.Batch.BatchNumber}.", "feed-consumption", consumption.Id, cancellationToken);
        return Ok(ToConsumptionDto(consumption));
    }

    [HttpPut("consumptions/{id:guid}")]
    public async Task<ActionResult<FeedConsumptionDto>> UpdateConsumption(Guid id, FeedConsumptionRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var consumption = await dbContext.FeedConsumptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (consumption is null)
        {
            return NotFound(new { detail = "Feed consumption record was not found." });
        }

        if (!await CanAccessCompanyAsync(consumption.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        RestoreStock(consumption.CompanyId, consumption.FeedConfigurationId, consumption.FeedType, consumption.BagSizeKg ?? 0, consumption.BagsUsed);
        var variant = await GetScopedVariantAsync(request.BatchVariantId, cancellationToken);
        if (variant is null)
        {
            return BadRequest(new { detail = "Select a valid batch color for your company." });
        }

        var feedConfig = await GetScopedFeedConfigurationAsync(request.FeedConfigurationId, cancellationToken);
        if (feedConfig is null)
        {
            return BadRequest(new { detail = "Select a valid feed type configuration." });
        }

        if (!await ConsumeStockAsync(variant.Batch!.CompanyId, feedConfig.Id, LegacyFeedTypeFromName(feedConfig.Name), feedConfig.BagSizeKg, request.BagsUsed, cancellationToken))
        {
            return BadRequest(new { detail = "Insufficient feed stock for the selected feed type and bag size." });
        }

        consumption.CompanyId = variant.Batch.CompanyId;
        consumption.BatchId = variant.BatchId;
        consumption.BatchVariantId = variant.Id;
        consumption.Date = request.Date;
        consumption.FeedConfigurationId = feedConfig.Id;
        consumption.FeedType = LegacyFeedTypeFromName(feedConfig.Name);
        consumption.BagSizeKg = feedConfig.BagSizeKg;
        consumption.BagsUsed = request.BagsUsed;
        consumption.AmountKg = feedConfig.BagSizeKg * request.BagsUsed;
        consumption.Notes = request.Notes;
        consumption.UpdatedByUserId = user.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, consumption.CompanyId, "Feed usage updated", $"{DisplayName(user)} updated a feed usage entry.", "feed-consumption", consumption.Id, cancellationToken);
        return Ok(ToConsumptionDto(consumption));
    }

    [HttpDelete("consumptions/{id:guid}")]
    public async Task<IActionResult> DeleteConsumption(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var consumption = await dbContext.FeedConsumptions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (consumption is null)
        {
            return NotFound(new { detail = "Feed consumption record was not found." });
        }

        if (!await CanAccessCompanyAsync(consumption.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        RestoreStock(consumption.CompanyId, consumption.FeedConfigurationId, consumption.FeedType, consumption.BagSizeKg ?? 0, consumption.BagsUsed);
        consumption.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, consumption.CompanyId, "Feed usage deleted", $"{DisplayName(user)} deleted a feed usage entry.", "feed-consumption", consumption.Id, cancellationToken);
        return NoContent();
    }

    [HttpPost("stocks")]
    public async Task<ActionResult<FeedStockLotDto>> CreateStock(FeedStockLotRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked || !scope.CompanyId.HasValue)
        {
            return Forbid();
        }

        var feedConfig = await GetScopedFeedConfigurationAsync(request.FeedConfigurationId, cancellationToken);
        if (feedConfig is null)
        {
            return BadRequest(new { detail = "Select a valid feed type configuration." });
        }

        if (request.BagsIn <= 0)
        {
            return BadRequest(new { detail = "Bags in must be greater than zero." });
        }

        var lot = new FeedStockLot
        {
            CompanyId = scope.CompanyId.Value,
            DateArrived = request.DateArrived,
            FeedConfigurationId = feedConfig.Id,
            FeedType = LegacyFeedTypeFromName(feedConfig.Name),
            Supplier = string.IsNullOrWhiteSpace(request.Supplier) ? "Unknown" : request.Supplier.Trim(),
            BagSizeKg = feedConfig.BagSizeKg,
            BagsIn = request.BagsIn,
            CostPerBag = request.CostPerBag,
            Condition = request.Condition,
            CreatedByUserId = user.Id
        };
        lot.Receive();

        await AddAggregateStockAsync(lot.CompanyId, lot.FeedType, lot.BagsIn * lot.BagSizeKg, lot.DateArrived, lot.Supplier, lot.CostPerBag / lot.BagSizeKg, cancellationToken);
        dbContext.FeedStockLots.Add(lot);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, lot.CompanyId, "Feed stock received", $"{DisplayName(user)} received {lot.BagsIn:N0} bags of {DisplayFeedType(lot.FeedType)} feed.", "feed-stock", lot.Id, cancellationToken);

        return Ok(ToStockLotDto(lot));
    }

    [HttpPut("stocks/{id:guid}")]
    public async Task<ActionResult<FeedStockLotDto>> UpdateStock(Guid id, FeedStockLotRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var lot = await dbContext.FeedStockLots.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lot is null)
        {
            return NotFound(new { detail = "Feed stock record was not found." });
        }

        if (!await CanAccessCompanyAsync(lot.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        if (lot.BagsRemaining != lot.BagsIn)
        {
            return BadRequest(new { detail = "This stock lot already has consumption history. Add a new stock lot or adjust consumption before editing." });
        }

        var feedConfig = await GetScopedFeedConfigurationAsync(request.FeedConfigurationId, cancellationToken);
        if (feedConfig is null)
        {
            return BadRequest(new { detail = "Select a valid feed type configuration." });
        }

        await AddAggregateStockAsync(lot.CompanyId, lot.FeedType, -(lot.BagsIn * lot.BagSizeKg), lot.DateArrived, lot.Supplier, 0, cancellationToken);
        lot.DateArrived = request.DateArrived;
        lot.FeedConfigurationId = feedConfig.Id;
        lot.FeedType = LegacyFeedTypeFromName(feedConfig.Name);
        lot.Supplier = string.IsNullOrWhiteSpace(request.Supplier) ? "Unknown" : request.Supplier.Trim();
        lot.BagSizeKg = feedConfig.BagSizeKg;
        lot.BagsIn = request.BagsIn;
        lot.CostPerBag = request.CostPerBag;
        lot.Condition = request.Condition;
        lot.UpdatedByUserId = user.Id;
        lot.Receive();
        await AddAggregateStockAsync(lot.CompanyId, lot.FeedType, lot.BagsIn * lot.BagSizeKg, lot.DateArrived, lot.Supplier, lot.CostPerBag / lot.BagSizeKg, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, lot.CompanyId, "Feed stock updated", $"{DisplayName(user)} updated a feed stock entry.", "feed-stock", lot.Id, cancellationToken);
        return Ok(ToStockLotDto(lot));
    }

    [HttpDelete("stocks/{id:guid}")]
    public async Task<IActionResult> DeleteStock(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var lot = await dbContext.FeedStockLots.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (lot is null)
        {
            return NotFound(new { detail = "Feed stock record was not found." });
        }

        if (!await CanAccessCompanyAsync(lot.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        await AddAggregateStockAsync(lot.CompanyId, lot.FeedType, -(lot.BagsRemaining * lot.BagSizeKg), lot.DateArrived, lot.Supplier, 0, cancellationToken);
        lot.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, lot.CompanyId, "Feed stock deleted", $"{DisplayName(user)} deleted a feed stock entry.", "feed-stock", lot.Id, cancellationToken);
        return NoContent();
    }

    private async Task<bool> ConsumeStockAsync(Guid companyId, Guid? feedConfigurationId, FeedType feedType, int bagSizeKg, int bags, CancellationToken cancellationToken)
    {
        var lots = await dbContext.FeedStockLots
            .Where(x => x.CompanyId == companyId &&
                        x.FeedType == feedType &&
                        x.BagSizeKg == bagSizeKg &&
                        (!feedConfigurationId.HasValue || x.FeedConfigurationId == feedConfigurationId.Value) &&
                        x.BagsRemaining > 0)
            .OrderBy(x => x.DateArrived)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        if (lots.Sum(x => x.BagsRemaining) < bags)
        {
            return false;
        }

        var remaining = bags;
        foreach (var lot in lots)
        {
            var take = Math.Min(remaining, lot.BagsRemaining);
            lot.Consume(take);
            remaining -= take;
            if (remaining == 0)
            {
                break;
            }
        }

        await AddAggregateStockAsync(companyId, feedType, -(bags * bagSizeKg), null, null, 0, cancellationToken);
        return true;
    }

    private void RestoreStock(Guid companyId, Guid? feedConfigurationId, FeedType feedType, int bagSizeKg, int bags)
    {
        if (bagSizeKg <= 0 || bags <= 0)
        {
            return;
        }

        var lot = dbContext.FeedStockLots
            .Where(x => x.CompanyId == companyId &&
                        x.FeedType == feedType &&
                        x.BagSizeKg == bagSizeKg &&
                        (!feedConfigurationId.HasValue || x.FeedConfigurationId == feedConfigurationId.Value))
            .OrderByDescending(x => x.DateArrived)
            .FirstOrDefault();

        lot?.Restore(bags);

        var stock = dbContext.FeedStocks.FirstOrDefault(x => x.CompanyId == companyId && x.FeedType == feedType);
        if (stock is not null)
        {
            stock.CurrentStockKg += bags * bagSizeKg;
        }
    }

    private async Task AddAggregateStockAsync(Guid companyId, FeedType feedType, decimal kgDelta, DateOnly? restockedDate, string? supplier, decimal costPerKg, CancellationToken cancellationToken)
    {
        var stock = await dbContext.FeedStocks.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.FeedType == feedType, cancellationToken);
        if (stock is null)
        {
            stock = new FeedStock { CompanyId = companyId, FeedType = feedType };
            dbContext.FeedStocks.Add(stock);
        }

        stock.CurrentStockKg = Math.Max(0, stock.CurrentStockKg + kgDelta);
        if (restockedDate.HasValue)
        {
            stock.LastRestockedDate = restockedDate;
        }

        if (!string.IsNullOrWhiteSpace(supplier))
        {
            stock.Supplier = supplier;
        }

        if (costPerKg > 0)
        {
            stock.CostPerKg = costPerKg;
        }
    }

    private async Task<BatchVariant?> GetScopedVariantAsync(Guid variantId, CancellationToken cancellationToken)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return null;
        }

        var query = dbContext.BatchVariants
            .Include(x => x.Batch)
            .Where(x => x.Id == variantId && x.Batch != null && x.Batch.Status == BatchStatus.Active);

        if (scope.CompanyId.HasValue)
        {
            query = query.Where(x => x.Batch!.CompanyId == scope.CompanyId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<FeedConfiguration?> GetScopedFeedConfigurationAsync(Guid feedConfigurationId, CancellationToken cancellationToken)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return null;
        }

        var query = dbContext.FeedConfigurations
            .Where(x => x.Id == feedConfigurationId && x.IsActive);

        if (scope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == scope.CompanyId.Value);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> CanAccessCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        return !scope.Blocked && (!scope.CompanyId.HasValue || scope.CompanyId.Value == companyId);
    }

    private async Task<CompanyScope> ResolveCompanyScopeAsync(CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSystemAdmin(User))
        {
            return new CompanyScope(null, false);
        }

        var user = await GetCurrentUserAsync();
        return user?.CompanyId is Guid companyId
            ? new CompanyScope(companyId, false)
            : new CompanyScope(null, true);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var id)
            ? await userManager.FindByIdAsync(id.ToString())
            : null;
    }

    private Task NotifyOperationAsync(ApplicationUser actor, Guid companyId, string title, string detail, string targetType, Guid targetId, CancellationToken cancellationToken)
    {
        var roles = actor.FarmRole == UserRole.Admin
            ? new[] { UserRole.Worker }
            : new[] { UserRole.Admin };

        return activityNotifier.NotifyCompanyAsync(
            companyId,
            title,
            detail,
            actor.Id,
            DisplayName(actor),
            "activity",
            targetType,
            targetId,
            roles,
            cancellationToken);
    }

    private static FeedConsumptionDto ToConsumptionDto(FeedConsumption record) => new(
        record.Id,
        record.CompanyId,
        record.BatchId,
        record.Batch?.BatchNumber ?? string.Empty,
        record.BatchVariantId,
        record.BatchVariant?.Color ?? VariantColor.Mixed,
        record.Date,
        record.FeedType,
        record.FeedConfigurationId,
        record.FeedConfiguration?.Name ?? DisplayFeedType(record.FeedType),
        record.BagSizeKg ?? 0,
        record.BagsUsed,
        record.AmountKg,
        record.Notes);

    private static FeedStockLotDto ToStockLotDto(FeedStockLot lot) => new(
        lot.Id,
        lot.CompanyId,
        lot.DateArrived,
        lot.FeedType,
        lot.FeedConfigurationId,
        lot.FeedConfiguration?.Name ?? DisplayFeedType(lot.FeedType),
        lot.Supplier,
        lot.BagSizeKg,
        lot.BagsIn,
        lot.BagsRemaining,
        lot.CostPerBag,
        lot.BagsIn * lot.CostPerBag,
        lot.BagsRemaining * lot.BagSizeKg,
        lot.Condition);

    private static string DisplayFeedType(FeedType feedType) =>
        feedType switch
        {
            FeedType.LayerPremium => "Layer Premium",
            FeedType.LayerStandard => "Layer Standard",
            FeedType.Starter => "Starter",
            FeedType.Grower => "Grower",
            _ => feedType.ToString()
        };

    private async Task EnsureDefaultFeedConfigurationsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var hasAny = await dbContext.FeedConfigurations.AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        if (hasAny)
        {
            return;
        }

        dbContext.FeedConfigurations.AddRange(
            new FeedConfiguration { CompanyId = companyId, Name = "Layer Premium", BagSizeKg = 50 },
            new FeedConfiguration { CompanyId = companyId, Name = "Layer Standard", BagSizeKg = 50 },
            new FeedConfiguration { CompanyId = companyId, Name = "Starter", BagSizeKg = 25 },
            new FeedConfiguration { CompanyId = companyId, Name = "Grower", BagSizeKg = 50 });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static FeedConfigurationDto ToConfigDto(FeedConfiguration config) => new(config.Id, config.CompanyId, config.Name, config.BagSizeKg, config.IsActive);

    private static FeedType LegacyFeedTypeFromName(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        if (normalized.Contains("starter"))
        {
            return FeedType.Starter;
        }

        if (normalized.Contains("grower"))
        {
            return FeedType.Grower;
        }

        if (normalized.Contains("standard"))
        {
            return FeedType.LayerStandard;
        }

        return FeedType.LayerPremium;
    }

    private static string DisplayName(ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "User" : name;
    }

    private sealed record CompanyScope(Guid? CompanyId, bool Blocked);
}

public sealed record FeedOptionsDto(IReadOnlyCollection<FeedTypeOptionDto> FeedTypes, IReadOnlyCollection<FeedConfigurationDto> Configurations);
public sealed record FeedTypeOptionDto(FeedType FeedType, string Label, IReadOnlyCollection<int> BagSizes);
public sealed record FeedConfigurationDto(Guid Id, Guid CompanyId, string Name, int BagSizeKg, bool IsActive);
public sealed record FeedMetricsDto(int TotalBagsInStock, decimal TotalKgInStock, decimal WeeklyAverageKg, decimal TodayUsageKg);
public sealed record PagedFeedConsumptionResponse(IReadOnlyCollection<FeedConsumptionDto> Items, int Total, int Page, int PageSize);
public sealed record PagedFeedStockLotResponse(IReadOnlyCollection<FeedStockLotDto> Items, int Total, int Page, int PageSize);

public sealed record FeedConsumptionDto(
    Guid Id,
    Guid CompanyId,
    Guid BatchId,
    string BatchNumber,
    Guid BatchVariantId,
    VariantColor BirdColor,
    DateOnly Date,
    FeedType FeedType,
    Guid? FeedConfigurationId,
    string FeedTypeLabel,
    int BagSizeKg,
    int BagsUsed,
    decimal TotalKg,
    string? Notes);

public sealed record FeedStockLotDto(
    Guid Id,
    Guid CompanyId,
    DateOnly DateArrived,
    FeedType FeedType,
    Guid? FeedConfigurationId,
    string FeedTypeLabel,
    string Supplier,
    int BagSizeKg,
    int BagsIn,
    int BagsRemaining,
    decimal CostPerBag,
    decimal TotalCost,
    decimal TotalKgRemaining,
    string? Condition);

public sealed record FeedConsumptionRequest(
    Guid BatchVariantId,
    DateOnly Date,
    Guid FeedConfigurationId,
    int BagsUsed,
    string? Notes);

public sealed record FeedStockLotRequest(
    DateOnly DateArrived,
    Guid FeedConfigurationId,
    string? Supplier,
    int BagsIn,
    decimal CostPerBag,
    string? Condition);

public sealed record FeedConfigurationRequest(string Name, int BagSizeKg, bool IsActive = true);
