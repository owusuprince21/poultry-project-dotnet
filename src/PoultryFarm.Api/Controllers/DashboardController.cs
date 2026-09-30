using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Application.Dashboard.DTOs;
using PoultryFarm.Domain.Common;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(
    IApplicationDbContext dbContext,
    ApplicationDbContext platformDbContext,
    IDateTimeProvider clock) : ControllerBase
{
    [Authorize]
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> Stats([FromQuery] Guid? company, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var weekAgo = today.AddDays(-7);
        var companyScope = company;

        if (!PermissionHelpers.IsSystemAdmin(User))
        {
            var companyClaim = PermissionHelpers.GetCompanyId(User);
            if (!Guid.TryParse(companyClaim, out var currentCompanyId))
            {
                return Forbid();
            }

            companyScope = currentCompanyId;
        }

        var activeBatches = dbContext.Batches.AsNoTracking().Where(x => x.Status == BatchStatus.Active);
        var eggProduction = dbContext.EggProductions.AsNoTracking().Where(x => x.Date == today);
        var feedConsumption = dbContext.FeedConsumptions.AsNoTracking().Where(x => x.Date >= weekAgo);
        var medications = dbContext.Medications.AsNoTracking().Where(x => x.ScheduledDate < today && x.Status == ScheduleStatus.Scheduled);
        var debeaking = dbContext.DebeakingSchedules.AsNoTracking().Where(x => x.ScheduledDate < today && x.Status == ScheduleStatus.Scheduled);
        var feedStock = dbContext.FeedStocks.AsNoTracking().Where(x => x.CurrentStockKg <= x.MinimumThresholdKg);
        var allFeedStock = dbContext.FeedStocks.AsNoTracking();
        var allEggProduction = dbContext.EggProductions.AsNoTracking();
        var allEggSaleItems = dbContext.EggSaleItems.AsNoTracking().Where(x => x.EggSale != null);
        var eggSales = dbContext.EggSales.AsNoTracking();
        var birdSales = dbContext.BirdSales.AsNoTracking();
        var healthEvents = dbContext.BirdHealthEvents.AsNoTracking().Where(x => x.Date >= weekAgo);

        if (companyScope.HasValue)
        {
            activeBatches = activeBatches.Where(x => x.CompanyId == companyScope.Value);
            eggProduction = eggProduction.Where(x => x.CompanyId == companyScope.Value);
            feedConsumption = feedConsumption.Where(x => x.CompanyId == companyScope.Value);
            medications = medications.Where(x => x.CompanyId == companyScope.Value);
            debeaking = debeaking.Where(x => x.CompanyId == companyScope.Value);
            feedStock = feedStock.Where(x => x.CompanyId == companyScope.Value);
            allFeedStock = allFeedStock.Where(x => x.CompanyId == companyScope.Value);
            allEggProduction = allEggProduction.Where(x => x.CompanyId == companyScope.Value);
            allEggSaleItems = allEggSaleItems.Where(x => x.EggSale!.CompanyId == companyScope.Value);
            eggSales = eggSales.Where(x => x.CompanyId == companyScope.Value);
            birdSales = birdSales.Where(x => x.CompanyId == companyScope.Value);
            healthEvents = healthEvents.Where(x => x.CompanyId == companyScope.Value);
        }

        var totalActiveBirds = await activeBatches.SumAsync(x => x.CurrentCount, cancellationToken);
        var activeBatchCount = await activeBatches.CountAsync(cancellationToken);
        var todayEggs = await eggProduction.SumAsync(
            x => x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs,
            cancellationToken);
        var weeklyFeed = await feedConsumption.SumAsync(x => x.AmountKg, cancellationToken);
        var currentBatch = await activeBatches.OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var expectedProduction = totalActiveBirds * 0.88m;
        var productionRate = expectedProduction <= 0 ? 0 : Math.Min(100, todayEggs / expectedProduction * 100);
        var producedEggs = await allEggProduction.SumAsync(
            x => x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs,
            cancellationToken);
        var soldEggs = await allEggSaleItems.SumAsync(x => x.Eggs, cancellationToken);
        var todayEggRevenue = await eggSales.Where(x => x.SaleDate == today).SumAsync(x => x.GrandTotal, cancellationToken);
        var todayBirdRevenue = await birdSales.Where(x => x.SaleDate == today).SumAsync(x => x.TotalAmount, cancellationToken);
        var weeklyEggRevenue = await eggSales.Where(x => x.SaleDate >= weekAgo).SumAsync(x => x.GrandTotal, cancellationToken);
        var weeklyBirdRevenue = await birdSales.Where(x => x.SaleDate >= weekAgo).SumAsync(x => x.TotalAmount, cancellationToken);
        var weeklyEggsSold = await allEggSaleItems.Where(x => x.EggSale!.SaleDate >= weekAgo).SumAsync(x => x.Eggs, cancellationToken);
        var weeklyBirdsSold = await birdSales.Where(x => x.SaleDate >= weekAgo).SumAsync(x => x.BirdsSold, cancellationToken);

        var trendStart = today.AddDays(-6);
        var eggByDay = await allEggProduction
            .Where(x => x.Date >= trendStart && x.Date <= today)
            .GroupBy(x => x.Date)
            .Select(g => new
            {
                Date = g.Key,
                Eggs = g.Sum(x => x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs)
            })
            .ToListAsync(cancellationToken);
        var feedTrend = dbContext.FeedConsumptions.AsNoTracking().Where(x => x.Date >= trendStart && x.Date <= today);
        if (companyScope.HasValue)
        {
            feedTrend = feedTrend.Where(x => x.CompanyId == companyScope.Value);
        }

        var feedByDay = await feedTrend
            .GroupBy(x => x.Date)
            .Select(g => new { Date = g.Key, Kg = g.Sum(x => x.AmountKg) })
            .ToListAsync(cancellationToken);
        var days = Enumerable.Range(0, 7)
            .Select(offset => trendStart.AddDays(offset))
            .Select(day => new FarmDayTrendDto(
                day.ToString("ddd d"),
                eggByDay.Where(x => x.Date == day).Sum(x => x.Eggs),
                feedByDay.Where(x => x.Date == day).Sum(x => x.Kg)))
            .ToArray();

        var sickBirds = dbContext.BirdHealthEvents.AsNoTracking().Where(x => x.Status == BirdHealthStatus.Sick);
        var deadBirds = dbContext.BirdHealthEvents.AsNoTracking().Where(x => x.Status == BirdHealthStatus.Dead);
        if (companyScope.HasValue)
        {
            sickBirds = sickBirds.Where(x => x.CompanyId == companyScope.Value);
            deadBirds = deadBirds.Where(x => x.CompanyId == companyScope.Value);
        }

        return Ok(new DashboardStatsDto(
            totalActiveBirds,
            todayEggs,
            weeklyFeed,
            await medications.CountAsync(cancellationToken),
            await debeaking.CountAsync(cancellationToken),
            await feedStock.CountAsync(cancellationToken),
            currentBatch?.AgeInWeeks(today) ?? 0,
            Math.Round(productionRate, 2),
            activeBatchCount,
            Math.Max(0, producedEggs - soldEggs),
            await allFeedStock.SumAsync(x => x.CurrentStockKg, cancellationToken),
            todayEggRevenue + todayBirdRevenue,
            weeklyEggRevenue + weeklyBirdRevenue,
            weeklyEggsSold,
            weeklyBirdsSold,
            await healthEvents.CountAsync(cancellationToken),
            Math.Max(0, await sickBirds.SumAsync(x => x.Count, cancellationToken)),
            await deadBirds.SumAsync(x => x.Count, cancellationToken),
            days));
    }

    [Authorize]
    [HttpGet("system-admin")]
    public async Task<ActionResult<SystemAdminDashboardOverviewDto>> SystemAdminOverview(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var startDate = today.AddDays(-13);
        var startDateTime = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var totalCompanies = await platformDbContext.Companies.CountAsync(cancellationToken);
        var activeCompanies = await platformDbContext.Companies.CountAsync(x => x.IsActive, cancellationToken);
        var totalUsers = await platformDbContext.Users.CountAsync(cancellationToken);
        var systemUsers = await platformDbContext.Users.CountAsync(x => x.IsSystemAdmin, cancellationToken);
        var farmAdmins = await platformDbContext.Users.CountAsync(x => !x.IsSystemAdmin && x.FarmRole == UserRole.Admin, cancellationToken);
        var workers = await platformDbContext.Users.CountAsync(x => !x.IsSystemAdmin && x.FarmRole == UserRole.Worker, cancellationToken);
        var blockedUsers = await platformDbContext.Users.CountAsync(x => x.LockoutEnd != null && x.LockoutEnd > DateTimeOffset.UtcNow, cancellationToken);
        var activeBatches = await platformDbContext.Batches.CountAsync(x => x.Status == BatchStatus.Active, cancellationToken);

        var activeBirds = await platformDbContext.Batches
            .Where(x => x.Status == BatchStatus.Active)
            .SumAsync(x => x.CurrentCount, cancellationToken);

        var todayEggs = await platformDbContext.EggProductions
            .Where(x => x.Date == today)
            .SumAsync(x => x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs, cancellationToken);

        var platformEvents = await platformDbContext.Users
            .AsNoTracking()
            .Where(x => x.CreatedAt >= startDateTime)
            .Select(x => new { x.CreatedAt, Type = "User" })
            .Concat(platformDbContext.Companies
                .AsNoTracking()
                .Where(x => x.CreatedAt >= startDateTime)
                .Select(x => new { x.CreatedAt, Type = "Company" }))
            .ToListAsync(cancellationToken);

        var eggProduction = await platformDbContext.EggProductions
            .AsNoTracking()
            .Where(x => x.Date >= startDate && x.Date <= today)
            .Select(x => new
            {
                x.Date,
                TotalEggs = x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs
            })
            .ToListAsync(cancellationToken);

        var feedConsumption = await platformDbContext.FeedConsumptions
            .AsNoTracking()
            .Where(x => x.Date >= startDate && x.Date <= today)
            .Select(x => new { x.Date, x.AmountKg })
            .ToListAsync(cancellationToken);

        var labels = Enumerable.Range(0, 14)
            .Select(offset => startDate.AddDays(offset))
            .ToArray();

        var labelText = labels.Select(x => x.ToString("MMM d")).ToArray();

        var companySeries = labels
            .Select(day => (decimal)platformEvents.Count(x => x.Type == "Company" && DateOnly.FromDateTime(x.CreatedAt.DateTime) == day))
            .ToArray();

        var userSeries = labels
            .Select(day => (decimal)platformEvents.Count(x => x.Type == "User" && DateOnly.FromDateTime(x.CreatedAt.DateTime) == day))
            .ToArray();

        var eggSeries = labels
            .Select(day => (decimal)eggProduction.Where(x => x.Date == day).Sum(x => x.TotalEggs))
            .ToArray();

        var feedSeries = labels
            .Select(day => feedConsumption.Where(x => x.Date == day).Sum(x => x.AmountKg))
            .ToArray();

        return Ok(new SystemAdminDashboardOverviewDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            Metrics:
            [
                new SystemAdminMetricDto("Companies", totalCompanies.ToString("N0"), activeCompanies.ToString("N0") + " active", "business", "Primary"),
                new SystemAdminMetricDto("Users", totalUsers.ToString("N0"), $"{systemUsers:N0} system / {farmAdmins:N0} admins / {workers:N0} workers", "groups", "Success"),
                new SystemAdminMetricDto("Active Batches", activeBatches.ToString("N0"), $"{activeBirds:N0} birds under management", "agriculture", "Warning"),
                new SystemAdminMetricDto("Security Alerts", blockedUsers.ToString("N0"), "blocked or locked accounts", "notifications_active", "Error")
            ],
            Charts:
            [
                new SystemAdminLineChartDto(
                    "Platform Growth",
                    "Company and user onboarding over the last 14 days.",
                    labelText,
                    [
                        new SystemAdminChartSeriesDto("Companies", companySeries),
                        new SystemAdminChartSeriesDto("Users", userSeries)
                    ]),
                new SystemAdminLineChartDto(
                    "Farm Operations",
                    "Egg production and feed consumption across all companies.",
                    labelText,
                    [
                        new SystemAdminChartSeriesDto("Eggs", eggSeries),
                        new SystemAdminChartSeriesDto("Feed kg", feedSeries)
                    ])
            ],
            Modules:
            [
                new SystemAdminModuleDto("Company tenancy", $"{activeCompanies:N0}/{totalCompanies:N0} active", "High", "Success"),
                new SystemAdminModuleDto("RBAC and user management", $"{totalUsers:N0} accounts", "High", "Success"),
                new SystemAdminModuleDto("Security controls", $"{blockedUsers:N0} blocked", "High", blockedUsers > 0 ? "Warning" : "Success"),
                new SystemAdminModuleDto("Farm operations oversight", $"{activeBatches:N0} active batches", "Medium", "Info")
            ]));
    }
}

public sealed record SystemAdminDashboardOverviewDto(
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<SystemAdminMetricDto> Metrics,
    IReadOnlyCollection<SystemAdminLineChartDto> Charts,
    IReadOnlyCollection<SystemAdminModuleDto> Modules);

public sealed record SystemAdminMetricDto(
    string Label,
    string Value,
    string Detail,
    string Icon,
    string Color);

public sealed record SystemAdminLineChartDto(
    string Title,
    string Subtitle,
    IReadOnlyCollection<string> Labels,
    IReadOnlyCollection<SystemAdminChartSeriesDto> Series);

public sealed record SystemAdminChartSeriesDto(
    string Name,
    IReadOnlyCollection<decimal> Data);

public sealed record SystemAdminModuleDto(
    string Name,
    string Status,
    string Priority,
    string Color);
