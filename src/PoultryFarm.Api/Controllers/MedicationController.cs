using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Schedules;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/medication")]
[Authorize]
public sealed class MedicationController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IActivityNotifier notifier) : ControllerBase
{
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
    {
        var companyId = await CurrentCompanyIdAsync(cancellationToken);
        if (!companyId.HasValue) return Forbid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var scheduled = await dbContext.Medications.CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Scheduled, cancellationToken);
        var completed = await dbContext.Medications.CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Completed, cancellationToken);
        var alerts = await dbContext.Medications.CountAsync(x => x.CompanyId == companyId && x.Status != ScheduleStatus.Completed && x.ScheduledDate <= today.AddDays(1), cancellationToken);
        return Ok(new { scheduled, completed, alerts });
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string type = "schedule", [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var companyId = await CurrentCompanyIdAsync(cancellationToken);
        if (!companyId.HasValue) return Forbid();
        var query = dbContext.Medications.AsNoTracking().Include(x => x.Batch).Include(x => x.BatchVariant).Where(x => x.CompanyId == companyId);
        query = type.Equals("daily", StringComparison.OrdinalIgnoreCase)
            ? query.Where(x => x.Status == ScheduleStatus.Completed)
            : query.Where(x => x.Status != ScheduleStatus.Completed);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.ScheduledDate).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).Select(x => ToDto(x)).ToListAsync(cancellationToken);
        return Ok(new { items, total, page, pageSize });
    }

    [HttpPost]
    public async Task<IActionResult> Create(MedicationRequest request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        if (user?.CompanyId is not Guid companyId) return Forbid();
        var variant = await dbContext.BatchVariants.Include(x => x.Batch).FirstOrDefaultAsync(x => x.Id == request.BatchVariantId && x.Batch != null && x.Batch.CompanyId == companyId, cancellationToken);
        if (variant is null) return BadRequest(new { detail = "Select a valid batch color." });

        var medication = new Medication
        {
            CompanyId = companyId,
            BatchId = variant.BatchId,
            BatchVariantId = variant.Id,
            MedicationName = request.MedicationName.Trim(),
            MedicationType = request.MedicationType.Trim(),
            Purpose = request.Purpose.Trim(),
            Dosage = request.Dosage.Trim(),
            Frequency = request.Frequency ?? string.Empty,
            ScheduledDate = request.Date,
            CompletedDate = request.IsDailyRecord ? request.Date : null,
            Status = request.IsDailyRecord ? ScheduleStatus.Completed : ScheduleStatus.Scheduled,
            Notes = request.Notes,
            AdministeredBy = request.IsDailyRecord ? DisplayName(user) : null,
            CreatedByUserId = user.Id
        };

        dbContext.Medications.Add(medication);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCompanyAsync(companyId, request.IsDailyRecord ? "Medication recorded" : "Medication scheduled", $"{DisplayName(user)} {(request.IsDailyRecord ? "recorded" : "scheduled")} {medication.MedicationName} ({medication.MedicationType}) for batch {variant.Batch!.BatchNumber} on {medication.ScheduledDate:yyyy-MM-dd}. Dosage: {medication.Dosage}. Purpose: {medication.Purpose}.", user.Id, DisplayName(user), targetType: "medication", targetId: medication.Id, cancellationToken: cancellationToken);
        return Ok(ToDto(medication));
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        if (user?.CompanyId is not Guid companyId) return Forbid();
        var medication = await dbContext.Medications.Include(x => x.Batch).FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (medication is null) return NotFound(new { detail = "Medication record was not found." });
        medication.Status = ScheduleStatus.Completed;
        medication.CompletedDate = DateOnly.FromDateTime(DateTime.UtcNow);
        medication.AdministeredBy = DisplayName(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCompanyAsync(companyId, "Medication completed", $"{DisplayName(user)} completed {medication.MedicationName} for batch {medication.Batch?.BatchNumber}.", user.Id, DisplayName(user), targetType: "medication", targetId: medication.Id, cancellationToken: cancellationToken);
        return Ok(ToDto(medication));
    }

    private async Task<Guid?> CurrentCompanyIdAsync(CancellationToken cancellationToken)
    {
        if (PermissionHelpers.IsSystemAdmin(User)) return null;
        var user = await CurrentUserAsync();
        return user?.CompanyId;
    }

    private async Task<ApplicationUser?> CurrentUserAsync()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? await userManager.FindByIdAsync(id.ToString()) : null;
    }

    private static object ToDto(Medication x) => new
    {
        x.Id,
        x.BatchId,
        BatchNumber = x.Batch == null ? "" : x.Batch.BatchNumber,
        x.BatchVariantId,
        BirdColor = x.BatchVariant == null ? VariantColor.Mixed : x.BatchVariant.Color,
        x.MedicationName,
        x.MedicationType,
        Date = x.ScheduledDate,
        x.Purpose,
        x.Dosage,
        x.Frequency,
        x.Status,
        x.CompletedDate,
        x.Notes
    };

    private static string DisplayName(ApplicationUser user) => string.IsNullOrWhiteSpace($"{user.FirstName} {user.LastName}".Trim()) ? user.UserName ?? "User" : $"{user.FirstName} {user.LastName}".Trim();
}

public sealed record MedicationRequest(Guid BatchVariantId, string MedicationName, DateOnly Date, string Purpose, string MedicationType, string Dosage, string? Frequency, string? Notes, bool IsDailyRecord);
