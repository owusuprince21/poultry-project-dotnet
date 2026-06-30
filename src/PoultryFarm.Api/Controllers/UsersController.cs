using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Domain.Audit;
using PoultryFarm.Domain.Common;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<UserManagementDto>>> Get(CancellationToken cancellationToken)
    {
        var currentRole = PermissionHelpers.GetNormalizedRole(User);
        if (currentRole is not AppRoles.SuperAdmin and not AppRoles.SubAdmin and not AppRoles.SystemAdmin and not AppRoles.FarmAdmin)
        {
            return Forbid();
        }

        var companyId = GetCurrentCompanyId();

        var query =
            from user in dbContext.Users.AsNoTracking()
            join company in dbContext.Companies.AsNoTracking()
                on user.CompanyId equals company.Id into companies
            from company in companies.DefaultIfEmpty()
            select new { user, company };

        if (!PermissionHelpers.IsSystemAdmin(User))
        {
            if (companyId is null)
            {
                return Forbid();
            }

            query = query.Where(x => x.user.CompanyId == companyId.Value);
        }

        var users = await query
            .OrderBy(x => x.company == null ? "System" : x.company.Name)
            .ThenBy(x => x.user.FirstName)
            .ThenBy(x => x.user.UserName)
            .Select(x => new UserManagementDto(
                x.user.Id,
                x.user.UserName ?? string.Empty,
                x.user.Email,
                x.user.FirstName,
                x.user.LastName,
                NormalizeRole(x.user),
                x.user.CompanyId,
                x.company == null ? null : x.company.Name,
                x.company == null ? null : x.company.Code,
                x.user.IsSystemAdmin,
                IsActive(x.user),
                x.user.MustChangePassword,
                x.user.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpGet("system")]
    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    public async Task<ActionResult<IReadOnlyCollection<UserManagementDto>>> GetSystemUsers(CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.IsSystemAdmin)
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.UserName)
            .Select(x => new UserManagementDto(
                x.Id,
                x.UserName ?? string.Empty,
                x.Email,
                x.FirstName,
                x.LastName,
                NormalizeRole(x),
                x.CompanyId,
                null,
                null,
                x.IsSystemAdmin,
                IsActive(x),
                x.MustChangePassword,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    [HttpPost("system")]
    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    public async Task<ActionResult<UserManagementDto>> CreateSystemUser(CreateSystemUserRequest request)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        if (ValidateCreateUserRequest(request) is { } validationError)
        {
            return BadRequest(new { detail = validationError });
        }

        var requestedRole = AppRoles.Normalize(request.Role);
        if (requestedRole is not AppRoles.SuperAdmin and not AppRoles.SubAdmin)
        {
            return BadRequest(new { role = "Role must be Super Admin or Sub Admin." });
        }

        var farmRole = requestedRole == AppRoles.SuperAdmin ? UserRole.SystemAdmin : UserRole.SubAdmin;
        var identityRole = requestedRole == AppRoles.SuperAdmin ? "SystemAdmin" : "SubAdmin";

        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            Email = request.Email?.Trim(),
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            FarmRole = farmRole,
            IsSystemAdmin = true,
            MustChangePassword = true,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, request.Password.Trim());
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        await userManager.AddToRoleAsync(user, identityRole);
        await AddUserAuditLogAsync(
            "System user created",
            user,
            null,
            $"Created {DisplayRole(NormalizeRole(user))} {DisplayName(user)} with username {user.UserName}.",
            "System User");

        return CreatedAtAction(nameof(GetSystemUsers), new { id = user.Id }, ToDto(user, null, null));
    }

    [HttpPost]
    public async Task<ActionResult<UserManagementDto>> Create(CreateManagedUserRequest request, CancellationToken cancellationToken)
    {
        if (ValidateCreateUserRequest(request) is { } validationError)
        {
            return BadRequest(new { detail = validationError });
        }

        var requestedRole = AppRoles.Normalize(request.Role);
        if (requestedRole is not AppRoles.FarmAdmin and not AppRoles.Worker)
        {
            return BadRequest(new { role = "Role must be Farm Admin or Worker." });
        }

        if (!PermissionHelpers.IsSystemAdmin(User) && !PermissionHelpers.IsCompanyAdmin(User))
        {
            return Forbid();
        }

        var companyId = PermissionHelpers.IsSystemAdmin(User)
            ? request.CompanyId
            : GetCurrentCompanyId();

        if (companyId is null || companyId == Guid.Empty)
        {
            return BadRequest(new { companyId = "Company is required." });
        }

        var company = await dbContext.Companies.FindAsync([companyId.Value], cancellationToken);
        if (company is null)
        {
            return BadRequest(new { companyId = "Selected company was not found." });
        }

        if (!company.IsActive)
        {
            return BadRequest(new { companyId = "Selected company is inactive." });
        }

        var farmRole = requestedRole == AppRoles.FarmAdmin ? UserRole.Admin : UserRole.Worker;
        var identityRole = requestedRole == AppRoles.FarmAdmin ? "Admin" : "Worker";

        var user = BuildUser(request, company.Id, farmRole);
        var result = await userManager.CreateAsync(user, request.Password.Trim());
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        await userManager.AddToRoleAsync(user, identityRole);
        await AddUserAuditLogAsync(
            "User created",
            user,
            company,
            $"Created {DisplayRole(requestedRole)} {DisplayName(user)} with username {user.UserName} for {company.Name}.",
            "Identity");

        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToDto(user, company.Name, company.Code));
    }

    [HttpPost("farm-admins")]
    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    public async Task<ActionResult<UserManagementDto>> CreateFarmAdmin(CreateFarmAdminRequest request, CancellationToken cancellationToken)
    {
        if (ValidateCreateUserRequest(request) is { } validationError)
        {
            return BadRequest(new { detail = validationError });
        }

        if (request.CompanyId == Guid.Empty)
        {
            return BadRequest(new { companyId = "Company is required." });
        }

        var company = await dbContext.Companies.FindAsync([request.CompanyId], cancellationToken);
        if (company is null)
        {
            return BadRequest(new { companyId = "Selected company was not found." });
        }

        if (!company.IsActive)
        {
            return BadRequest(new { companyId = "Selected company is inactive." });
        }

        var user = BuildUser(request, request.CompanyId, UserRole.Admin);
        var result = await userManager.CreateAsync(user, request.Password.Trim());
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        await userManager.AddToRoleAsync(user, "Admin");
        await AddUserAuditLogAsync(
            "User created",
            user,
            company,
            $"Created Farm Admin {DisplayName(user)} with username {user.UserName} for {company.Name}.",
            "Identity");

        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToDto(user, company.Name, company.Code));
    }

    [HttpPost("workers")]
    public async Task<ActionResult<UserManagementDto>> CreateWorker(CreateWorkerRequest request, CancellationToken cancellationToken)
    {
        if (ValidateCreateUserRequest(request) is { } validationError)
        {
            return BadRequest(new { detail = validationError });
        }

        if (!PermissionHelpers.IsSystemAdmin(User) && !PermissionHelpers.IsCompanyAdmin(User))
        {
            return Forbid();
        }

        var companyId = PermissionHelpers.IsSystemAdmin(User)
            ? request.CompanyId
            : GetCurrentCompanyId();

        if (companyId is null || companyId == Guid.Empty)
        {
            return BadRequest(new { companyId = "Company is required." });
        }

        var company = await dbContext.Companies.FindAsync([companyId.Value], cancellationToken);
        if (company is null)
        {
            return BadRequest(new { companyId = "Selected company was not found." });
        }

        if (!company.IsActive)
        {
            return BadRequest(new { companyId = "Selected company is inactive." });
        }

        var user = BuildUser(request, company.Id, UserRole.Worker);
        var result = await userManager.CreateAsync(user, request.Password.Trim());
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        await userManager.AddToRoleAsync(user, "Worker");
        await AddUserAuditLogAsync(
            "User created",
            user,
            company,
            $"Created Worker {DisplayName(user)} with username {user.UserName} for {company.Name}.",
            "Identity");

        return CreatedAtAction(nameof(Get), new { id = user.Id }, ToDto(user, company.Name, company.Code));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserManagementDto>> Update(Guid id, UpdateManagedUserRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound(new { detail = "User was not found." });
        }

        if (!CanManageUser(user))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            return BadRequest(new { firstName = "First name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { lastName = "Last name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new { username = "Username is required." });
        }

        var username = request.Username.Trim();
        if (!string.Equals(user.UserName, username, StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await userManager.FindByNameAsync(username);
            if (existingUser is not null && existingUser.Id != user.Id)
            {
                return BadRequest(new { username = "Username is already taken." });
            }

            var usernameResult = await userManager.SetUserNameAsync(user, username);
            if (!usernameResult.Succeeded)
            {
                return BadRequest(new { detail = usernameResult.Errors.Select(x => x.Description).ToArray() });
            }
        }

        var requestedRole = AppRoles.Normalize(request.Role);

        if (user.IsSystemAdmin)
        {
            if (!PermissionHelpers.IsSuperAdmin(User))
            {
                return Forbid();
            }

            var systemRole = AppRoles.Normalize(request.Role);
            if (systemRole is not AppRoles.SuperAdmin and not AppRoles.SubAdmin and not AppRoles.SystemAdmin)
            {
                return BadRequest(new { role = "Role must be Super Admin or Sub Admin." });
            }

            user.CompanyId = null;
            user.FarmRole = systemRole == AppRoles.SubAdmin ? UserRole.SubAdmin : UserRole.SystemAdmin;
            await SyncSystemRoleAsync(user, user.FarmRole == UserRole.SubAdmin ? "SubAdmin" : "SystemAdmin");
        }
        else
        {
            if (requestedRole is not AppRoles.FarmAdmin and not AppRoles.Worker)
            {
                return BadRequest(new { role = "Role must be Farm Admin or Worker." });
            }

            var companyId = PermissionHelpers.IsSystemAdmin(User)
                ? request.CompanyId
                : GetCurrentCompanyId();

            if (companyId is null || companyId == Guid.Empty)
            {
                return BadRequest(new { companyId = "Company is required." });
            }

            var companyExists = await dbContext.Companies.AnyAsync(x => x.Id == companyId.Value && x.IsActive, cancellationToken);
            if (!companyExists)
            {
                return BadRequest(new { companyId = "Selected active company was not found." });
            }

            user.CompanyId = companyId.Value;
            user.FarmRole = requestedRole == AppRoles.FarmAdmin ? UserRole.Admin : UserRole.Worker;
            await SyncRoleAsync(user, requestedRole);
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email?.Trim();

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        var company = user.CompanyId.HasValue
            ? await dbContext.Companies.FindAsync([user.CompanyId.Value], cancellationToken)
            : null;

        await AddUserAuditLogAsync(
            "User updated",
            user,
            company,
            $"Updated {DisplayRole(NormalizeRole(user))} {DisplayName(user)} with username {user.UserName}{(company is null ? "." : $" for {company.Name}.")}",
            user.IsSystemAdmin ? "System User" : "Identity");

        return Ok(ToDto(user, company?.Name, company?.Code));
    }

    [HttpPost("{id:guid}/block")]
    public async Task<IActionResult> Block(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound(new { detail = "User was not found." });
        }

        if (IsCurrentUser(user))
        {
            return BadRequest(new { detail = "You cannot block your own account." });
        }

        if (!CanManageUser(user))
        {
            return Forbid();
        }

        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        var company = user.CompanyId.HasValue
            ? await dbContext.Companies.FindAsync(user.CompanyId.Value)
            : null;

        await AddUserAuditLogAsync(
            "User blocked",
            user,
            company,
            $"Blocked {DisplayRole(NormalizeRole(user))} {DisplayName(user)} with username {user.UserName}{(company is null ? "." : $" for {company.Name}.")}",
            user.IsSystemAdmin ? "System User" : "Identity");

        return Ok(new { detail = "User blocked successfully." });
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound(new { detail = "User was not found." });
        }

        if (!CanManageUser(user))
        {
            return Forbid();
        }

        user.LockoutEnabled = true;
        user.LockoutEnd = null;
        await userManager.ResetAccessFailedCountAsync(user);
        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        var company = user.CompanyId.HasValue
            ? await dbContext.Companies.FindAsync(user.CompanyId.Value)
            : null;

        await AddUserAuditLogAsync(
            "User activated",
            user,
            company,
            $"Activated {DisplayRole(NormalizeRole(user))} {DisplayName(user)} with username {user.UserName}{(company is null ? "." : $" for {company.Name}.")}",
            user.IsSystemAdmin ? "System User" : "Identity");

        return Ok(new { detail = "User activated successfully." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound(new { detail = "User was not found." });
        }

        if (IsCurrentUser(user))
        {
            return BadRequest(new { detail = "You cannot delete your own account." });
        }

        if (!CanManageUser(user))
        {
            return Forbid();
        }

        var company = user.CompanyId.HasValue
            ? await dbContext.Companies.FindAsync(user.CompanyId.Value)
            : null;

        var originalUsername = user.UserName;
        var originalEmail = user.Email;
        user.IsDeleted = true;
        user.DeletedAt = DateTimeOffset.UtcNow;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.UserName = $"deleted-{user.Id:N}";
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.Email = null;
        user.NormalizedEmail = null;
        user.PhoneNumber = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        await AddUserAuditLogAsync(
            "User deleted",
            user,
            company,
            $"Deleted {DisplayRole(NormalizeRole(user))} {DisplayName(user)} with username {originalUsername ?? originalEmail ?? "unknown"}{(company is null ? "." : $" from {company.Name}.")}",
            user.IsSystemAdmin ? "System User" : "Identity");

        return Ok(new { detail = "User deleted successfully." });
    }

    private Guid? GetCurrentCompanyId()
    {
        var companyId = PermissionHelpers.GetCompanyId(User);
        return Guid.TryParse(companyId, out var parsed) ? parsed : null;
    }

    private Guid? GetCurrentUserId()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userId, out var parsed) ? parsed : null;
    }

    private bool IsCurrentUser(ApplicationUser user) => GetCurrentUserId() == user.Id;

    private bool CanManageUser(ApplicationUser user)
    {
        if (PermissionHelpers.IsSuperAdmin(User))
        {
            return true;
        }

        if (PermissionHelpers.IsSystemAdmin(User))
        {
            return !user.IsSystemAdmin;
        }

        var companyId = GetCurrentCompanyId();
        return companyId.HasValue &&
               user.CompanyId == companyId.Value &&
               !user.IsSystemAdmin &&
               user.FarmRole is UserRole.Admin or UserRole.Worker;
    }

    private async Task SyncRoleAsync(ApplicationUser user, string normalizedRole)
    {
        var targetRole = normalizedRole == AppRoles.FarmAdmin ? "Admin" : "Worker";
        var roles = await userManager.GetRolesAsync(user);

        foreach (var role in roles.Where(x => x is "Admin" or "Worker" or "SystemAdmin"))
        {
            if (role != targetRole)
            {
                await userManager.RemoveFromRoleAsync(user, role);
            }
        }

        if (!await userManager.IsInRoleAsync(user, targetRole))
        {
            await userManager.AddToRoleAsync(user, targetRole);
        }
    }

    private async Task SyncSystemRoleAsync(ApplicationUser user, string targetRole)
    {
        var roles = await userManager.GetRolesAsync(user);

        foreach (var role in roles.Where(x => x is "Admin" or "Worker" or "SystemAdmin" or "SubAdmin"))
        {
            if (role != targetRole)
            {
                await userManager.RemoveFromRoleAsync(user, role);
            }
        }

        if (!await userManager.IsInRoleAsync(user, targetRole))
        {
            await userManager.AddToRoleAsync(user, targetRole);
        }
    }

    private static ApplicationUser BuildUser(CreateUserRequest request, Guid companyId, UserRole farmRole)
    {
        return new ApplicationUser
        {
            UserName = request.Username.Trim(),
            Email = request.Email?.Trim(),
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            CompanyId = companyId,
            FarmRole = farmRole,
            IsSystemAdmin = false,
            MustChangePassword = true,
            EmailConfirmed = true
        };
    }

    private static string? ValidateCreateUserRequest(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return "Username is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return "Temporary password is required.";
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            return "First name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            return "Last name is required.";
        }

        return null;
    }

    private static UserManagementDto ToDto(ApplicationUser user, string? companyName, string? companyCode)
    {
        return new UserManagementDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email,
            user.FirstName,
            user.LastName,
            NormalizeRole(user),
            user.CompanyId,
            companyName,
            companyCode,
            user.IsSystemAdmin,
            IsActive(user),
            user.MustChangePassword,
            user.CreatedAt);
    }

    private static bool IsActive(ApplicationUser user) =>
        user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow;

    private async Task AddUserAuditLogAsync(string action, ApplicationUser targetUser, Domain.Companies.Company? company, string detail, string category)
    {
        var actor = await GetCurrentUserAsync();

        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = GetCurrentUserId(),
            ActorName = actor is null ? User.Identity?.Name ?? "System user" : DisplayName(actor),
            ActorUsername = actor?.UserName ?? User.Identity?.Name ?? string.Empty,
            ActorRole = PermissionHelpers.GetNormalizedRole(User),
            CompanyId = company?.Id,
            CompanyName = company?.Name,
            Action = action,
            TargetType = "User",
            TargetId = targetUser.Id,
            TargetName = DisplayName(targetUser),
            TargetUsername = targetUser.UserName,
            Detail = detail,
            Category = category
        });

        await dbContext.SaveChangesAsync();
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
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "User" : name;
    }

    private static string DisplayRole(string role) =>
        role switch
        {
            AppRoles.SuperAdmin => "Super Admin",
            AppRoles.SubAdmin => "Sub Admin",
            AppRoles.FarmAdmin => "Farm Admin",
            AppRoles.Worker => "Worker",
            _ => role
        };

    private static string NormalizeRole(ApplicationUser user)
    {
        if (user.IsSystemAdmin)
        {
            return user.FarmRole == UserRole.SubAdmin ? AppRoles.SubAdmin : AppRoles.SuperAdmin;
        }

        return user.FarmRole == UserRole.Admin ? AppRoles.FarmAdmin : AppRoles.Worker;
    }
}

public abstract record CreateUserRequest(
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string? Email);

public sealed record CreateFarmAdminRequest(
    Guid CompanyId,
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string? Email) : CreateUserRequest(Username, Password, FirstName, LastName, Email);

public sealed record CreateWorkerRequest(
    Guid? CompanyId,
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string? Email) : CreateUserRequest(Username, Password, FirstName, LastName, Email);

public sealed record CreateManagedUserRequest(
    Guid? CompanyId,
    string Role,
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string? Email) : CreateUserRequest(Username, Password, FirstName, LastName, Email);

public sealed record CreateSystemUserRequest(
    string Role,
    string Username,
    string Password,
    string FirstName,
    string LastName,
    string? Email) : CreateUserRequest(Username, Password, FirstName, LastName, Email);

public sealed record UpdateManagedUserRequest(
    Guid? CompanyId,
    string Role,
    string Username,
    string FirstName,
    string LastName,
    string? Email);

public sealed record UserManagementDto(
    Guid Id,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    string Role,
    Guid? CompanyId,
    string? CompanyName,
    string? CompanyCode,
    bool IsSystemAdmin,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt);
