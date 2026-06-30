using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Schedules;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/debeaking")]
[Authorize]
public sealed class DebeakingController(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager, IActivityNotifier notifier) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(new
        {
            scheduled = await dbContext.DebeakingSchedules.CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Scheduled, cancellationToken),
            completed = await dbContext.DebeakingSchedules.CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Completed, cancellationToken),
            overdue = await dbContext.DebeakingSchedules.CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Scheduled && x.ScheduledDate < today, cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var companyId = await CompanyIdAsync();
        if (!companyId.HasValue) return Forbid();
        var query = dbContext.DebeakingSchedules.AsNoTracking().Include(x => x.Batch).Include(x => x.BatchVariant).Where(x => x.CompanyId == companyId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.ScheduledDate).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).Select(x => ToDto(x)).ToListAsync(cancellationToken);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost]
    public async Task<IActionResult> Create(DebeakingRequest request, CancellationToken cancellationToken)
    {
        var user = await UserAsync();
        if (user?.CompanyId is not Guid companyId) return Forbid();
        var variant = await dbContext.BatchVariants.Include(x => x.Batch).FirstOrDefaultAsync(x => x.Id == request.BatchVariantId && x.Batch != null && x.Batch.CompanyId == companyId, cancellationToken);
        if (variant?.Batch is null) return BadRequest(new { detail = "Select a valid batch color." });
        var schedule = new DebeakingSchedule { CompanyId = companyId, BatchId = variant.BatchId, BatchVariantId = variant.Id, DebeakingType = request.DebeakingType.Trim(), ScheduledDate = request.ScheduledDate, BirdAgeWeeks = request.BirdAgeWeeks, Notes = request.Notes, CreatedByUserId = user.Id };
        dbContext.DebeakingSchedules.Add(schedule);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCompanyAsync(companyId, "Debeaking scheduled", $"{DisplayName(user)} scheduled {schedule.DebeakingType} debeaking for batch {variant.Batch.BatchNumber} on {schedule.ScheduledDate:yyyy-MM-dd}.", user.Id, DisplayName(user), targetType: "debeaking", targetId: schedule.Id, cancellationToken: cancellationToken);
        return Ok(ToDto(schedule));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var user = await UserAsync();
        if (user?.CompanyId is not Guid companyId) return Forbid();
        var schedule = await dbContext.DebeakingSchedules.Include(x => x.Batch).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (schedule is null) return NotFound(new { detail = "Debeaking schedule was not found." });
        schedule.Status = ScheduleStatus.Completed;
        schedule.CompletedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        schedule.PerformedBy = DisplayName(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(schedule));
    }

    private async Task<Guid?> CompanyIdAsync() => (await UserAsync())?.CompanyId;
    private async Task<ApplicationUser?> UserAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
    }
    private static object ToDto(DebeakingSchedule x) => new { x.Id, x.BatchId, BatchNumber = x.Batch == null ? "" : x.Batch.BatchNumber, x.BatchVariantId, BirdColor = x.BatchVariant == null ? VariantColor.Mixed : x.BatchVariant.Color, x.DebeakingType, x.ScheduledDate, x.CompletedDate, x.BirdAgeWeeks, x.Status, x.Notes };
    private static string DisplayName(ApplicationUser user) => string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim()) ? user.UserName ?? "User" : $"{user.FirstName} {user.LastName}".Trim();
}

public sealed record DebeakingRequest(Guid BatchVariantId, string DebeakingType, DateOnly ScheduledDate, int BirdAgeWeeks, string? Notes);
