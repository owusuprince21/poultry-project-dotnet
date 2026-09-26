using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Api.Services;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Health;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/bird-health")]
[Authorize]
public sealed class BirdHealthController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IActivityNotifier notifier,
    ControllerAudit audit) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics([FromQuery] Guid? company, CancellationToken cancellationToken)
    {
        var companyId = await ResolveCompanyIdAsync(company, cancellationToken);
        if (!PermissionHelpers.IsSystemAdmin(User) && !companyId.HasValue)
        {
            return Forbid();
        }

        var sickQuery = dbContext.BirdHealthEvents.Where(x => x.Status == BirdHealthStatus.Sick);
        var deadQuery = dbContext.BirdHealthEvents.Where(x => x.Status == BirdHealthStatus.Dead);
        if (companyId.HasValue)
        {
            sickQuery = sickQuery.Where(x => x.CompanyId == companyId.Value);
            deadQuery = deadQuery.Where(x => x.CompanyId == companyId.Value);
        }

        var sick = await sickQuery.SumAsync(x => x.Count, cancellationToken);
        var dead = await deadQuery.SumAsync(x => x.Count, cancellationToken);
        return Ok(new { sickBirds = Math.Max(0, sick), deadBirds = dead });
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? company,
        [FromQuery] string type = "sick",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var companyId = await ResolveCompanyIdAsync(company, cancellationToken);
        if (!PermissionHelpers.IsSystemAdmin(User) && !companyId.HasValue)
        {
            return Forbid();
        }

        var status = type.Equals("dead", StringComparison.OrdinalIgnoreCase) ? BirdHealthStatus.Dead : BirdHealthStatus.Sick;
        var query = dbContext.BirdHealthEvents.AsNoTracking().Include(x => x.Batch).Include(x => x.BatchVariant).Where(x => x.Status == status);
        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.Date).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).Select(x => ToDto(x)).ToListAsync(cancellationToken);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost]
    public async Task<IActionResult> Create(HealthRequest request, CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var user = await UserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var variant = await dbContext.BatchVariants
            .Include(x => x.Batch)
                .ThenInclude(x => x!.Variants)
            .FirstOrDefaultAsync(x => x.Id == request.BatchVariantId && x.Batch != null, cancellationToken);
        if (variant?.Batch is null)
        {
            return BadRequest(new { detail = "Select a valid batch color." });
        }

        var companyId = await ResolveCompanyIdForWriteAsync(variant.Batch.CompanyId, cancellationToken);
        if (!companyId.HasValue || companyId.Value != variant.Batch.CompanyId)
        {
            return Forbid();
        }

        var status = request.Status == "dead" ? BirdHealthStatus.Dead : BirdHealthStatus.Sick;
        if (status == BirdHealthStatus.Dead)
        {
            variant.DecreaseCurrentCount(request.Count);
            variant.Batch.SyncCountsFromVariants();
        }

        var health = new BirdHealthEvent
        {
            CompanyId = companyId.Value,
            BatchId = variant.BatchId,
            BatchVariantId = variant.Id,
            Date = request.Date,
            BirdType = variant.Batch.BirdType,
            Status = status,
            Count = request.Count,
            Cause = request.Cause,
            CreatedByUserId = user.Id
        };
        dbContext.BirdHealthEvents.Add(health);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCompanyAsync(companyId.Value, status == BirdHealthStatus.Dead ? "Bird mortality recorded" : "Sick birds recorded", $"{DisplayName(user)} recorded {request.Count:N0} {status.ToString().ToLowerInvariant()} birds in batch {variant.Batch.BatchNumber} ({variant.Color}). Cause: {request.Cause}.", user.Id, DisplayName(user), targetType: "bird-health", targetId: health.Id, cancellationToken: cancellationToken);
        await WriteHealthAuditAsync("Create", health, variant.Batch.BatchNumber, cancellationToken);
        return Ok(ToDto(health));
    }

    [HttpPost("{id:guid}/resolve-sick")]
    public async Task<IActionResult> ResolveSick(Guid id, ResolveSickRequest request, CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var user = await UserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var original = await dbContext.BirdHealthEvents
            .Include(x => x.Batch)
                .ThenInclude(x => x!.Variants)
            .Include(x => x.BatchVariant)
            .FirstOrDefaultAsync(x => x.Id == id && x.Status == BirdHealthStatus.Sick, cancellationToken);
        if (original?.BatchVariant is null || original.Batch is null)
        {
            return NotFound(new { detail = "Sick bird record was not found." });
        }

        var companyId = await ResolveCompanyIdForWriteAsync(original.CompanyId, cancellationToken);
        if (!companyId.HasValue || companyId.Value != original.CompanyId)
        {
            return Forbid();
        }

        if (request.Died < 0 || request.Recovered < 0 || request.Died + request.Recovered <= 0)
        {
            return BadRequest(new { detail = "Enter recovered or dead count." });
        }

        var activeSickAdjustment = new BirdHealthEvent { CompanyId = companyId.Value, BatchId = original.BatchId, BatchVariantId = original.BatchVariantId, Date = DateOnly.FromDateTime(DateTime.UtcNow), BirdType = original.BirdType, Status = BirdHealthStatus.Sick, Count = -(request.Died + request.Recovered), Cause = $"Resolved from sick record: {request.Note}" };
        dbContext.BirdHealthEvents.Add(activeSickAdjustment);
        if (request.Died > 0)
        {
            original.BatchVariant.DecreaseCurrentCount(request.Died);
            original.Batch.SyncCountsFromVariants();
            dbContext.BirdHealthEvents.Add(new BirdHealthEvent { CompanyId = companyId.Value, BatchId = original.BatchId, BatchVariantId = original.BatchVariantId, Date = DateOnly.FromDateTime(DateTime.UtcNow), BirdType = original.BirdType, Status = BirdHealthStatus.Dead, Count = request.Died, Cause = $"Died after sickness: {request.Note}" });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCompanyAsync(companyId.Value, "Sick bird record updated", $"{DisplayName(user)} resolved sick birds for batch {original.Batch.BatchNumber}: {request.Recovered:N0} recovered, {request.Died:N0} died.", user.Id, DisplayName(user), targetType: "bird-health", targetId: original.Id, cancellationToken: cancellationToken);
        await WriteHealthAuditAsync("Update", original, original.Batch.BatchNumber, cancellationToken);
        return Ok(new { detail = "Sick bird record resolved." });
    }

    private async Task<Guid?> ResolveCompanyIdAsync(Guid? requestedCompany, CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSystemAdmin(User))
        {
            return requestedCompany;
        }

        var user = await UserAsync();
        if (user?.CompanyId is not Guid companyId)
        {
            return null;
        }

        if (requestedCompany.HasValue && requestedCompany.Value != companyId)
        {
            return null;
        }

        return companyId;
    }

    private async Task<Guid?> ResolveCompanyIdForWriteAsync(Guid resourceCompanyId, CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSystemAdmin(User))
        {
            return resourceCompanyId;
        }

        var user = await UserAsync();
        return user?.CompanyId == resourceCompanyId ? resourceCompanyId : null;
    }

    private async Task WriteHealthAuditAsync(string action, BirdHealthEvent health, string batchNumber, CancellationToken cancellationToken)
    {
        var companyName = await dbContext.Companies.AsNoTracking()
            .Where(x => x.Id == health.CompanyId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken);

        await audit.WriteAsync(
            action,
            "Operations",
            "BirdHealth",
            batchNumber,
            $"{action} bird health record ({health.Status}, {health.Count:N0} birds).",
            health.Id,
            health.CompanyId,
            companyName,
            cancellationToken: cancellationToken);
    }

    private async Task<ApplicationUser?> UserAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
    }

    private static object ToDto(BirdHealthEvent x) => new { x.Id, x.BatchId, BatchNumber = x.Batch == null ? "" : x.Batch.BatchNumber, x.BatchVariantId, BirdColor = x.BatchVariant == null ? VariantColor.Mixed : x.BatchVariant.Color, x.Date, x.Status, x.Count, x.Cause };
    private static string DisplayName(ApplicationUser user) => string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim()) ? user.UserName ?? "User" : $"{user.FirstName} {user.LastName}".Trim();
}

public sealed record HealthRequest(Guid BatchVariantId, DateOnly Date, string Status, int Count, string Cause);
public sealed record ResolveSickRequest(int Recovered, int Died, string? Note);
