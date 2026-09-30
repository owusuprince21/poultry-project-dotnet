using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Api.Services;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Operations;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IFarmAssistantAgent assistantAgent,
    IActivityNotifier activityNotifier,
    ControllerAudit audit) : ControllerBase
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
    public async Task<ActionResult<IReadOnlyList<DailyObservationDto>>> Daily([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue)
        {
            return Forbid();
        }

        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await dbContext.DailyObservations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Date == day)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new DailyObservationDto(x.Id, x.Date, x.Category, x.Notes, x.Recommendation, x.AuthorName, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("daily")]
    public async Task<ActionResult<DailyObservationDto>> CreateDaily(DailyObservationRequest request, CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue)
        {
            return Forbid();
        }

        var notes = request.Notes?.Trim() ?? string.Empty;
        if (notes.Length < 8)
        {
            return BadRequest(new { detail = "Write what you observed. A few words is not enough for the assistant to act on." });
        }

        if (!Enum.IsDefined(request.Category))
        {
            return BadRequest(new { detail = "Select eggs, feed, birds, or a general observation." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = Guid.TryParse(userId, out var parsedId) ? await userManager.FindByIdAsync(parsedId.ToString()) : null;
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var author = $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(author))
        {
            author = user.UserName ?? "Worker";
        }

        var categoryLabel = CategoryLabel(request.Category);
        var recommendation = await assistantAgent.AdviseOnObservationAsync(companyId.Value, author, categoryLabel, request.Date, notes, cancellationToken);
        if (recommendation.Length > 4000)
        {
            recommendation = recommendation[..4000];
        }

        var observation = new DailyObservation
        {
            CompanyId = companyId.Value,
            Date = request.Date,
            Category = request.Category,
            Notes = notes.Length > 4000 ? notes[..4000] : notes,
            Recommendation = recommendation,
            AuthorName = author,
            CreatedByUserId = user.Id
        };
        dbContext.DailyObservations.Add(observation);
        await dbContext.SaveChangesAsync(cancellationToken);

        var detail = $"""
            Observation:
            {Trim(notes, 700)}

            What to do:
            {Trim(AdviceOnly(recommendation), 2500)}
            """;
        if (detail.Length > 4000)
        {
            detail = detail[..4000];
        }

        await activityNotifier.NotifyCompanyAsync(
            companyId.Value,
            $"{categoryLabel}: action from farm assistant",
            detail,
            user.Id,
            author,
            kind: "assistant",
            targetType: "daily-observation",
            targetId: observation.Id,
            recipientRoles: [UserRole.Admin, UserRole.Worker],
            includeActor: true,
            cancellationToken: cancellationToken);

        await audit.WriteAsync("Create", "Operations", "DailyObservation", categoryLabel, $"{author} recorded a {categoryLabel.ToLowerInvariant()}.", observation.Id, companyId, cancellationToken: cancellationToken);

        return Ok(new DailyObservationDto(observation.Id, observation.Date, observation.Category, observation.Notes, observation.Recommendation, observation.AuthorName, observation.CreatedAt));
    }

    private static string CategoryLabel(DailyObservationCategory category) =>
        category switch
        {
            DailyObservationCategory.Eggs => "Eggs observation",
            DailyObservationCategory.Feed => "Feed observation",
            DailyObservationCategory.Birds => "Birds observation",
            _ => "General observation"
        };

    private static string Trim(string value, int max) =>
        value.Length <= max ? value.Trim() : value.Trim()[..max] + "...";

    private static string AdviceOnly(string recommendation)
    {
        var text = recommendation.Trim();
        var repeat = text.IndexOf("Observation:", StringComparison.OrdinalIgnoreCase);
        if (repeat > 0)
        {
            text = text[..repeat].Trim();
        }

        return string.IsNullOrWhiteSpace(text)
            ? "Check the flock, separate anything that looks abnormal, and review today's feed and medication schedule before the next round."
            : text;
    }

    private async Task<Guid?> CompanyIdAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
        return user?.CompanyId;
    }
}

public sealed record DailyObservationDto(
    Guid Id,
    DateOnly Date,
    DailyObservationCategory Category,
    string Notes,
    string? Recommendation,
    string AuthorName,
    DateTimeOffset CreatedAt);

public sealed record DailyObservationRequest(DateOnly Date, DailyObservationCategory Category, string Notes);
