using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Production;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/egg-production")]
[Authorize]
public sealed class EggProductionController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IActivityNotifier activityNotifier) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedEggProductionResponse>> Get(
        [FromQuery] Guid? batchId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return Forbid();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = dbContext.EggProductions
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Where(x => !scope.CompanyId.HasValue || x.CompanyId == scope.CompanyId.Value);

        if (batchId.HasValue)
        {
            query = query.Where(x => x.BatchId == batchId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);

        return Ok(new PagedEggProductionResponse(rows, total, page, pageSize));
    }

    [HttpGet("metrics")]
    public async Task<ActionResult<EggProductionMetricsDto>> Metrics(
        [FromQuery] Guid? batchId,
        [FromQuery] EggSize? size,
        [FromQuery] VariantColor? color,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveCompanyScopeAsync(cancellationToken);
        if (scope.Blocked)
        {
            return Forbid();
        }

        var query = dbContext.EggProductions
            .AsNoTracking()
            .Include(x => x.BatchVariant)
            .Where(x => !scope.CompanyId.HasValue || x.CompanyId == scope.CompanyId.Value);

        if (batchId.HasValue)
        {
            query = query.Where(x => x.BatchId == batchId.Value);
        }

        if (color.HasValue)
        {
            query = query.Where(x => x.BatchVariant != null && x.BatchVariant.Color == color.Value);
        }

        var rows = await query.ToListAsync(cancellationToken);
        var totalEggs = rows.Sum(x => x.TotalEggs);
        var selectedSizeEggs = size switch
        {
            EggSize.Small => rows.Sum(x => x.SmallEggs),
            EggSize.Medium => rows.Sum(x => x.MediumEggs),
            EggSize.Large => rows.Sum(x => x.LargeEggs),
            EggSize.ExtraLarge => rows.Sum(x => x.ExtraLargeEggs),
            EggSize.Unsorted => rows.Sum(x => x.UnsortedEggs),
            _ => totalEggs
        };

        return Ok(new EggProductionMetricsDto(
            totalEggs,
            Math.Round(totalEggs / (decimal)EggProduction.EggsPerCrate, 2),
            selectedSizeEggs,
            rows.Count,
            rows.Sum(x => x.SmallEggs),
            rows.Sum(x => x.MediumEggs),
            rows.Sum(x => x.LargeEggs),
            rows.Sum(x => x.ExtraLargeEggs),
            rows.Sum(x => x.UnsortedEggs)));
    }

    [HttpPost]
    public async Task<ActionResult<EggProductionDto>> Create(EggProductionRequest request, CancellationToken cancellationToken)
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

        var production = new EggProduction
        {
            CompanyId = variant.Batch!.CompanyId,
            BatchId = variant.BatchId,
            BatchVariantId = variant.Id,
            Date = request.Date,
            EggColor = variant.EggColor,
            CollectionType = request.CollectionType,
            SmallCrates = request.SmallCrates,
            SmallPieces = request.SmallPieces,
            MediumCrates = request.MediumCrates,
            MediumPieces = request.MediumPieces,
            LargeCrates = request.LargeCrates,
            LargePieces = request.LargePieces,
            ExtraLargeCrates = request.JumboCrates,
            ExtraLargePieces = request.JumboPieces,
            UnsortedCrates = request.UnsortedCrates,
            UnsortedPieces = request.UnsortedPieces,
            Notes = request.Notes,
            CreatedByUserId = user.Id
        };
        production.RecalculateTotals();

        dbContext.EggProductions.Add(production);
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, production.CompanyId, "Egg production recorded", BuildEggProductionNotificationDetail(user, production, variant), "egg-production", production.Id, cancellationToken);

        return Ok(ToDto(production));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EggProductionDto>> Update(Guid id, EggProductionRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var production = await dbContext.EggProductions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (production is null)
        {
            return NotFound(new { detail = "Egg production record was not found." });
        }

        if (!await CanAccessCompanyAsync(production.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        var variant = await GetScopedVariantAsync(request.BatchVariantId, cancellationToken);
        if (variant is null)
        {
            return BadRequest(new { detail = "Select a valid batch color for your company." });
        }

        production.CompanyId = variant.Batch!.CompanyId;
        production.BatchId = variant.BatchId;
        production.BatchVariantId = variant.Id;
        production.Date = request.Date;
        production.EggColor = variant.EggColor;
        production.CollectionType = request.CollectionType;
        production.SmallCrates = request.SmallCrates;
        production.SmallPieces = request.SmallPieces;
        production.MediumCrates = request.MediumCrates;
        production.MediumPieces = request.MediumPieces;
        production.LargeCrates = request.LargeCrates;
        production.LargePieces = request.LargePieces;
        production.ExtraLargeCrates = request.JumboCrates;
        production.ExtraLargePieces = request.JumboPieces;
        production.UnsortedCrates = request.UnsortedCrates;
        production.UnsortedPieces = request.UnsortedPieces;
        production.Notes = request.Notes;
        production.UpdatedByUserId = user.Id;
        production.RecalculateTotals();

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, production.CompanyId, "Egg production updated", BuildEggProductionNotificationDetail(user, production, variant), "egg-production", production.Id, cancellationToken);
        return Ok(ToDto(production));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var production = await dbContext.EggProductions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (production is null)
        {
            return NotFound(new { detail = "Egg production record was not found." });
        }

        if (!await CanAccessCompanyAsync(production.CompanyId, cancellationToken))
        {
            return Forbid();
        }

        production.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyOperationAsync(user, production.CompanyId, "Egg production deleted", $"{DisplayName(user)} deleted an egg production entry.", "egg-production", production.Id, cancellationToken);
        return NoContent();
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
            .Where(x => x.Id == variantId && x.Batch != null && x.Batch.BirdType == BirdType.Layer && x.Batch.Status == BatchStatus.Active);

        if (scope.CompanyId.HasValue)
        {
            query = query.Where(x => x.Batch!.CompanyId == scope.CompanyId.Value);
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

    private static EggProductionDto ToDto(EggProduction production) => new(
        production.Id,
        production.CompanyId,
        production.BatchId,
        production.Batch?.BatchNumber ?? string.Empty,
        production.BatchVariantId,
        production.BatchVariant?.Color ?? VariantColor.Mixed,
        production.EggColor,
        production.Date,
        production.CollectionType,
        production.SmallCrates,
        production.SmallPieces,
        production.MediumCrates,
        production.MediumPieces,
        production.LargeCrates,
        production.LargePieces,
        production.ExtraLargeCrates,
        production.ExtraLargePieces,
        production.UnsortedCrates,
        production.UnsortedPieces,
        production.SmallEggs,
        production.MediumEggs,
        production.LargeEggs,
        production.ExtraLargeEggs,
        production.UnsortedEggs,
        production.TotalEggs,
        production.Notes);

    private static string BuildEggProductionNotificationDetail(ApplicationUser user, EggProduction production, BatchVariant variant)
    {
        var totalCrates = production.TotalEggs / EggProduction.EggsPerCrate;
        var totalPieces = production.TotalEggs % EggProduction.EggsPerCrate;
        var header = $"{DisplayName(user)} recorded egg production for batch {variant.Batch?.BatchNumber ?? "Unknown"} ({variant.Batch?.Breed ?? "unknown breed"}), bird color {variant.Color}, on {production.Date:yyyy-MM-dd}.";

        if (production.CollectionType == EggCollectionType.Unsorted)
        {
            return $"""
{header}
Collection type: Unsorted
Unsorted eggs: {production.UnsortedCrates:N0} crates and {production.UnsortedPieces:N0} pieces ({production.UnsortedEggs:N0} eggs)
Overall total: {totalCrates:N0} crates and {totalPieces:N0} pieces ({production.TotalEggs:N0} eggs)
Observation: {Blank(production.Notes)}
""";
        }

        return $"""
{header}
Collection type: Sorted
Small eggs: {production.SmallCrates:N0} crates and {production.SmallPieces:N0} pieces ({production.SmallEggs:N0} eggs)
Medium eggs: {production.MediumCrates:N0} crates and {production.MediumPieces:N0} pieces ({production.MediumEggs:N0} eggs)
Large eggs: {production.LargeCrates:N0} crates and {production.LargePieces:N0} pieces ({production.LargeEggs:N0} eggs)
Jumbo eggs: {production.ExtraLargeCrates:N0} crates and {production.ExtraLargePieces:N0} pieces ({production.ExtraLargeEggs:N0} eggs)
Overall total: {totalCrates:N0} crates and {totalPieces:N0} pieces ({production.TotalEggs:N0} eggs)
Observation: {Blank(production.Notes)}
""";
    }

    private static string DisplayName(ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "User" : name;
    }

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();

    private sealed record CompanyScope(Guid? CompanyId, bool Blocked);
}

public sealed record PagedEggProductionResponse(IReadOnlyCollection<EggProductionDto> Items, int Total, int Page, int PageSize);

public sealed record EggProductionMetricsDto(
    int TotalEggs,
    decimal TotalCrates,
    int SelectedSizeEggs,
    int Entries,
    int SmallEggs,
    int MediumEggs,
    int LargeEggs,
    int JumboEggs,
    int UnsortedEggs);

public sealed record EggProductionDto(
    Guid Id,
    Guid CompanyId,
    Guid BatchId,
    string BatchNumber,
    Guid BatchVariantId,
    VariantColor BirdColor,
    EggColor EggColor,
    DateOnly Date,
    EggCollectionType CollectionType,
    int SmallCrates,
    int SmallPieces,
    int MediumCrates,
    int MediumPieces,
    int LargeCrates,
    int LargePieces,
    int JumboCrates,
    int JumboPieces,
    int UnsortedCrates,
    int UnsortedPieces,
    int SmallEggs,
    int MediumEggs,
    int LargeEggs,
    int JumboEggs,
    int UnsortedEggs,
    int TotalEggs,
    string? Notes);

public sealed record EggProductionRequest(
    Guid BatchVariantId,
    DateOnly Date,
    EggCollectionType CollectionType,
    int SmallCrates,
    int SmallPieces,
    int MediumCrates,
    int MediumPieces,
    int LargeCrates,
    int LargePieces,
    int JumboCrates,
    int JumboPieces,
    int UnsortedCrates,
    int UnsortedPieces,
    string? Notes);
