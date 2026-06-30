using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Domain.Common;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(Policy = AppPolicies.SystemAdminOnly)]
public sealed class PlatformController(ApplicationDbContext dbContext) : ControllerBase
{
    [HttpGet("tenants")]
    public async Task<ActionResult<IReadOnlyCollection<TenantOverviewDto>>> Tenants([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var companyQuery = dbContext.Companies.AsNoTracking();
        if (from.HasValue)
        {
            companyQuery = companyQuery.Where(x => x.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            companyQuery = companyQuery.Where(x => x.CreatedAt <= to.Value);
        }

        var companies = await companyQuery
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Code,
                x.Email,
                x.Phone,
                x.IsActive,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.CompanyId.HasValue)
            .GroupBy(x => x.CompanyId!.Value)
            .Select(x => new
            {
                CompanyId = x.Key,
                TotalUsers = x.Count(),
                FarmAdmins = x.Count(user => user.FarmRole == UserRole.Admin),
                Workers = x.Count(user => user.FarmRole == UserRole.Worker),
                BlockedUsers = x.Count(user => user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow)
            })
            .ToListAsync(cancellationToken);

        var batches = await dbContext.Batches
            .AsNoTracking()
            .GroupBy(x => x.CompanyId)
            .Select(x => new
            {
                CompanyId = x.Key,
                ActiveBatches = x.Count(batch => batch.Status == BatchStatus.Active),
                ActiveBirds = x.Where(batch => batch.Status == BatchStatus.Active).Sum(batch => batch.CurrentCount)
            })
            .ToListAsync(cancellationToken);

        var tenantRows = companies
            .Select(company =>
            {
                var userSummary = users.FirstOrDefault(x => x.CompanyId == company.Id);
                var batchSummary = batches.FirstOrDefault(x => x.CompanyId == company.Id);

                return new TenantOverviewDto(
                    company.Id,
                    company.Name,
                    company.Code,
                    company.Email,
                    company.Phone,
                    company.IsActive,
                    company.CreatedAt,
                    userSummary?.TotalUsers ?? 0,
                    userSummary?.FarmAdmins ?? 0,
                    userSummary?.Workers ?? 0,
                    userSummary?.BlockedUsers ?? 0,
                    batchSummary?.ActiveBatches ?? 0,
                    batchSummary?.ActiveBirds ?? 0);
            })
            .ToList();

        return Ok(tenantRows);
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<IReadOnlyCollection<AuditLogEntryDto>>> AuditLogs([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var query = dbContext.AuditLogs.AsNoTracking();
        if (from.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= to.Value);
        }

        var auditRows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(250)
            .Select(x => new AuditLogEntryDto(
                x.CreatedAt,
                x.Action,
                string.IsNullOrWhiteSpace(x.ActorUsername) ? x.ActorName : $"{x.ActorName} ({x.ActorUsername})",
                x.ActorRole,
                string.IsNullOrWhiteSpace(x.TargetUsername) ? x.TargetName : $"{x.TargetName} ({x.TargetUsername})",
                x.Detail,
                x.Category,
                x.CompanyName))
            .ToListAsync(cancellationToken);

        if (auditRows.Count > 0)
        {
            return Ok(auditRows);
        }

        var companyEvents = await dbContext.Companies
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Take(40)
            .Select(x => new AuditLogEntryDto(
                x.UpdatedAt ?? x.CreatedAt,
                x.UpdatedAt == null ? "Company created" : "Company updated",
                "System",
                "Super Admin",
                x.Name,
                x.IsActive ? "Active tenant record" : "Inactive tenant record",
                "Company",
                x.Name))
            .ToListAsync(cancellationToken);

        var userEvents = await dbContext.Users
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(40)
            .Select(x => new AuditLogEntryDto(
                x.CreatedAt,
                "User provisioned",
                "System",
                x.IsSystemAdmin ? "System User" : x.FarmRole.ToString(),
                x.UserName ?? "User",
                x.LockoutEnd != null && x.LockoutEnd > DateTimeOffset.UtcNow ? "Blocked account" : "Active account",
                "Identity",
                null))
            .ToListAsync(cancellationToken);

        var fallbackRows = companyEvents
            .Concat(userEvents)
            .OrderByDescending(x => x.Timestamp)
            .Take(80)
            .ToList();

        return Ok(fallbackRows);
    }
}

public sealed record TenantOverviewDto(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int TotalUsers,
    int FarmAdmins,
    int Workers,
    int BlockedUsers,
    int ActiveBatches,
    int ActiveBirds);

public sealed record AuditLogEntryDto(
    DateTimeOffset Timestamp,
    string Event,
    string Actor,
    string Role,
    string Target,
    string Detail,
    string Category,
    string? CompanyName);
