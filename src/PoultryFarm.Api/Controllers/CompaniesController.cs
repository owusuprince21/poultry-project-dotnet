using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Companies.DTOs;
using PoultryFarm.Domain.Audit;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/companies")]
[Authorize]
public sealed class CompaniesController(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CompanyDto>>> Get(CancellationToken cancellationToken)
    {
        var query = dbContext.Companies.AsNoTracking();

        if (!PermissionHelpers.IsSystemAdmin(User))
        {
            var companyIdClaim = PermissionHelpers.GetCompanyId(User);
            if (!Guid.TryParse(companyIdClaim, out var companyId))
            {
                return Forbid();
            }

            query = query.Where(x => x.Id == companyId);
        }

        if (PermissionHelpers.IsSystemAdmin(User))
        {
            await CollapseDuplicateFarmsAsync(cancellationToken);
        }

        var companies = await query
            .OrderBy(x => x.Name)
            .Select(x => new CompanyDto(x.Id, x.Name, x.Code, x.Email, x.Phone, x.Address, x.IsActive))
            .ToListAsync(cancellationToken);

        return Ok(companies);
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpPost]
    public async Task<ActionResult<CompanyDto>> Create(CreateCompanyRequest request, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { name = "Company name is required." });
        }

        var normalizedName = request.Name.Trim().ToLowerInvariant();
        if (await dbContext.Companies.AnyAsync(x => x.Name.ToLower() == normalizedName, cancellationToken))
        {
            return BadRequest(new { name = "A farm with this name already exists." });
        }

        var company = new Company
        {
            Name = request.Name.Trim(),
            Code = await NextCompanyCodeAsync(cancellationToken),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            Address = request.Address?.Trim()
        };

        dbContext.Companies.Add(company);
        await AddAuditLogAsync("Company created", company, $"Created company {company.Name} with code {company.Code}.");
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = new CompanyDto(company.Id, company.Name, company.Code, company.Email, company.Phone, company.Address, company.IsActive);
        return CreatedAtAction(nameof(Get), new { id = company.Id }, dto);
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Update(Guid id, UpdateCompanyRequest request, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { name = "Company name is required." });
        }

        var company = await dbContext.Companies.FindAsync([id], cancellationToken);
        if (company is null)
        {
            return NotFound(new { detail = "Company was not found." });
        }

        company.Name = request.Name.Trim();
        company.Email = request.Email?.Trim();
        company.Phone = request.Phone?.Trim();
        company.Address = request.Address?.Trim();
        company.IsActive = request.IsActive;

        await AddAuditLogAsync("Company updated", company, $"Updated company {company.Name} ({company.Code}). Status: {(company.IsActive ? "Active" : "Inactive")}.");
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(company));
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var company = await dbContext.Companies.FindAsync([id], cancellationToken);
        if (company is null)
        {
            return NotFound(new { detail = "Company was not found." });
        }

        if (await HasLinkedDataAsync(id, cancellationToken))
        {
            return BadRequest(new { detail = "Company has users or farm records and cannot be deleted. Deactivate it instead." });
        }

        await AddAuditLogAsync("Company deleted", company, $"Deleted company {company.Name} ({company.Code}).");
        dbContext.Companies.Remove(company);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { detail = "Company deleted successfully." });
    }

    private async Task<bool> HasOperationalDataAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.Batches.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.EggProductions.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.FeedConsumptions.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.FeedStocks.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.EggSales.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.BirdSales.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.BirdHealthEvents.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.Medications.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.DebeakingSchedules.AnyAsync(x => x.CompanyId == companyId, cancellationToken)
            || await dbContext.MarketplaceListings.AnyAsync(x => x.CompanyId == companyId, cancellationToken);
    }

    private async Task<bool> HasLinkedDataAsync(Guid companyId, CancellationToken cancellationToken)
    {
        return await dbContext.Users.AnyAsync(x => x.CompanyId == companyId && !x.IsDeleted, cancellationToken)
            || await HasOperationalDataAsync(companyId, cancellationToken);
    }

    private async Task CollapseDuplicateFarmsAsync(CancellationToken cancellationToken)
    {
        var companies = await dbContext.Companies
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var group in companies.GroupBy(x => x.Name.Trim().ToLowerInvariant()).Where(x => x.Count() > 1))
        {
            var members = group.ToList();
            var ids = members.Select(x => x.Id).ToList();
            var registrationCompanyIds = await dbContext.FarmerRegistrations
                .AsNoTracking()
                .Where(x => x.CompanyId != null && ids.Contains(x.CompanyId.Value))
                .Select(x => x.CompanyId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            var userCounts = await dbContext.Users
                .AsNoTracking()
                .Where(x => x.CompanyId != null && ids.Contains(x.CompanyId.Value) && !x.IsDeleted)
                .GroupBy(x => x.CompanyId!.Value)
                .Select(x => new { CompanyId = x.Key, Count = x.Count() })
                .ToListAsync(cancellationToken);

            var keeper = members
                .OrderByDescending(x => registrationCompanyIds.Contains(x.Id))
                .ThenByDescending(x => userCounts.FirstOrDefault(count => count.CompanyId == x.Id)?.Count ?? 0)
                .ThenBy(x => x.CreatedAt)
                .First();

            foreach (var extra in members.Where(x => x.Id != keeper.Id))
            {
                if (await HasOperationalDataAsync(extra.Id, cancellationToken))
                {
                    continue;
                }

                var users = await dbContext.Users.Where(x => x.CompanyId == extra.Id).ToListAsync(cancellationToken);
                foreach (var user in users)
                {
                    user.CompanyId = keeper.Id;
                }

                var registrations = await dbContext.FarmerRegistrations.Where(x => x.CompanyId == extra.Id).ToListAsync(cancellationToken);
                foreach (var registration in registrations)
                {
                    registration.CompanyId = keeper.Id;
                }

                extra.IsDeleted = true;
                changed = true;
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<string> NextCompanyCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = GenerateCompanyCode();
            if (!await dbContext.Companies.AnyAsync(x => x.Code == code, cancellationToken))
            {
                return code;
            }
        }

        return GenerateCompanyCode();
    }

    private static CompanyDto ToDto(Company company) =>
        new(company.Id, company.Name, company.Code, company.Email, company.Phone, company.Address, company.IsActive);

    private static string GenerateCompanyCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 10).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    private async Task AddAuditLogAsync(string action, Company company, string detail)
    {
        var actor = await GetCurrentUserAsync();
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = GetCurrentUserId(),
            ActorName = actor is null ? User.Identity?.Name ?? "System user" : DisplayName(actor),
            ActorUsername = actor?.UserName ?? User.Identity?.Name ?? string.Empty,
            ActorRole = PermissionHelpers.GetNormalizedRole(User),
            CompanyId = company.Id,
            CompanyName = company.Name,
            Action = action,
            TargetType = "Company",
            TargetId = company.Id,
            TargetName = company.Name,
            Detail = detail,
            Category = "Company"
        });
    }

    private Guid? GetCurrentUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var parsed) ? parsed : null;
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var currentUserId = GetCurrentUserId();
        return currentUserId.HasValue
            ? await userManager.FindByIdAsync(currentUserId.Value.ToString())
            : null;
    }

    private static string DisplayName(ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "System user" : name;
    }
}

public sealed record UpdateCompanyRequest(
    string Name,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive);
