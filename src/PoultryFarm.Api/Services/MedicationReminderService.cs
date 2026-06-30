using Microsoft.EntityFrameworkCore;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Services;

public sealed class MedicationReminderService(
    IServiceScopeFactory scopeFactory,
    ILogger<MedicationReminderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendDueTomorrowRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Medication reminder scan failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    private async Task SendDueTomorrowRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IActivityNotifier>();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var schedules = await dbContext.Medications
            .Include(x => x.Batch)
            .Where(x =>
                x.Status == ScheduleStatus.Scheduled &&
                x.ScheduledDate == tomorrow &&
                x.ReminderSentAt == null)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var schedule in schedules)
        {
            await notifier.NotifyCompanyAsync(
                schedule.CompanyId,
                "Medication due tomorrow",
                $"{schedule.MedicationName} ({schedule.MedicationType}) is due tomorrow for batch {schedule.Batch?.BatchNumber ?? "selected batch"}. Dosage: {schedule.Dosage}. Purpose: {schedule.Purpose}.",
                kind: "medication",
                targetType: "medication",
                targetId: schedule.Id,
                recipientRoles: [UserRole.Admin, UserRole.Worker],
                cancellationToken: cancellationToken);

            schedule.ReminderSentAt = DateTimeOffset.UtcNow;
        }

        if (schedules.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
