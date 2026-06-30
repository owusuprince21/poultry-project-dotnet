using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PoultryFarm.Application.Companies.DTOs;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new { username = "Username is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { password = "Password is required." });
        }

        var username = request.Username.Trim();

        var user = await userManager.FindByNameAsync(username);

        if (user is null || user.IsDeleted)
        {
            return BadRequest(new { detail = "Invalid username or password." });
        }

        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return BadRequest(new { detail = "This account is blocked. Contact your administrator." });
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            var lockoutResult = await RegisterFailedLoginAttemptAsync(user);
            if (lockoutResult.IsBlocked)
            {
                return BadRequest(new { detail = "Too many failed login attempts. This account has been blocked. Contact your administrator." });
            }

            return BadRequest(new { detail = $"Invalid username or password. {lockoutResult.RemainingAttempts} attempt(s) remaining." });
        }

        Company? company = null;

        if (user.CompanyId.HasValue)
        {
            company = await dbContext.Companies.FindAsync(user.CompanyId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.CompanyCode))
        {
            if (company is null)
            {
                return BadRequest(new { companyCode = "This user is not assigned to any company." });
            }

            if (!string.Equals(company.Code, request.CompanyCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { companyCode = "Company code is invalid for this user." });
            }

            if (!company.IsActive)
            {
                return BadRequest(new { companyCode = "This company account is inactive." });
            }
        }

        await ResetFailedLoginAttemptsAsync(user);

        if (user.TwoFactorEnabled)
        {
            return Ok(new
            {
                requires2Fa = true,
                username = user.UserName,
                userId = user.Id,
                message = "Two-factor authentication code required."
            });
        }

        return Ok(new
        {
            requires2Fa = false,
            token = CreateToken(user, company),
            user = ToUserDto(user, company)
        });
    }

    private async Task<LoginAttemptResult> RegisterFailedLoginAttemptAsync(ApplicationUser user)
    {
        if (!await userManager.GetLockoutEnabledAsync(user))
        {
            await userManager.SetLockoutEnabledAsync(user, true);
        }

        await userManager.AccessFailedAsync(user);

        var failedAttempts = await userManager.GetAccessFailedCountAsync(user);
        var maximumAttempts = userManager.Options.Lockout.MaxFailedAccessAttempts;

        if (failedAttempts >= maximumAttempts || await userManager.IsLockedOutAsync(user))
        {
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            return new LoginAttemptResult(true, 0);
        }

        return new LoginAttemptResult(false, Math.Max(0, maximumAttempts - failedAttempts));
    }

    private async Task ResetFailedLoginAttemptsAsync(ApplicationUser user)
    {
        if (await userManager.GetAccessFailedCountAsync(user) > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }
    }

    [HttpGet("user")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out var id))
        {
            return Unauthorized(new { detail = "Invalid authenticated user." });
        }

        var user = await userManager.FindByIdAsync(id.ToString());

        if (user is null || user.IsDeleted)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        Company? company = null;

        if (user.CompanyId.HasValue)
        {
            company = await dbContext.Companies.FindAsync(user.CompanyId.Value);
        }

        return Ok(ToUserDto(user, company));
    }

    [HttpPost("2fa/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new { username = "Username is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { password = "Password is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { code = "Verification code is required." });
        }

        var user = await userManager.FindByNameAsync(request.Username.Trim());
        if (user is null || user.IsDeleted)
        {
            return BadRequest(new { detail = "Invalid username or password." });
        }

        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return BadRequest(new { detail = "This account is blocked. Contact your administrator." });
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            var lockoutResult = await RegisterFailedLoginAttemptAsync(user);
            if (lockoutResult.IsBlocked)
            {
                return BadRequest(new { detail = "Too many failed login attempts. This account has been blocked. Contact your administrator." });
            }

            return BadRequest(new { detail = $"Invalid username or password. {lockoutResult.RemainingAttempts} attempt(s) remaining." });
        }

        var code = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var validCode = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!validCode)
        {
            return BadRequest(new { detail = "Invalid two-factor authentication code." });
        }

        await ResetFailedLoginAttemptsAsync(user);

        Company? company = null;
        if (user.CompanyId.HasValue)
        {
            company = await dbContext.Companies.FindAsync(user.CompanyId.Value);
        }

        return Ok(new
        {
            requires2Fa = false,
            token = CreateToken(user, company),
            user = ToUserDto(user, company)
        });
    }

    [HttpPost("password/force-change")]
    [Authorize]
    public async Task<IActionResult> ForceChangePassword([FromBody] ForcePasswordChangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { newPassword = "New password is required." });
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { confirmPassword = "New passwords do not match." });
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest(new { currentPassword = "Current password is required." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id))
        {
            return Unauthorized(new { detail = "Invalid authenticated user." });
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        return Ok(new { detail = "Password changed successfully." });
    }

    private string CreateToken(ApplicationUser user, Company? company)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? "development-signing-key-change-before-production-12345";

        var issuer = configuration["Jwt:Issuer"] ?? "PoultryFarm.Api";
        var audience = configuration["Jwt:Audience"] ?? "PoultryFarm.Client";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var normalizedRole = NormalizeRole(user);
        var isSuperAdmin = IsSuperAdmin(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Role, normalizedRole),
            new("role", normalizedRole),
            new("is_system_admin", user.IsSystemAdmin ? "true" : "false"),
            new("is_superuser", isSuperAdmin ? "true" : "false")
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        if (company is not null)
        {
            claims.Add(new Claim("company_id", company.Id.ToString()));
            claims.Add(new Claim("company_code", company.Code));
            claims.Add(new Claim("company_name", company.Name));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static object ToUserDto(ApplicationUser user, Company? company)
    {
        return new
        {
            id = user.Id,
            username = user.UserName,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            role = NormalizeRole(user),
            company = company is null
                ? null
                : new CompanyDto(
                    company.Id,
                    company.Name,
                    company.Code,
                    company.Email,
                    company.Phone,
                    company.Address,
                    company.IsActive
                ),
            isActive = true,
            isSystemAdmin = user.IsSystemAdmin,
            isSuperuser = IsSuperAdmin(user),
            mustChangePassword = user.MustChangePassword,
            twoFactorEnabled = user.TwoFactorEnabled
        };
    }

    private static string NormalizeRole(ApplicationUser user)
    {
        if (user.IsSystemAdmin)
        {
            return IsSuperAdmin(user) ? "super_admin" : "sub_admin";
        }

        var rawRole = user.FarmRole.ToString()
            .Trim()
            .ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");

        return rawRole switch
        {
            "farmadmin" => "farm_admin",
            "companyadmin" => "farm_admin",
            "company_admin" => "farm_admin",
            "admin" => "farm_admin",

            "farmworker" => "worker",
            "farm_worker" => "worker",

            "systemadmin" => "system_admin",
            "system_admin" => "system_admin",

            _ => rawRole
        };
    }

    private static bool IsSuperAdmin(ApplicationUser user) =>
        user.IsSystemAdmin && user.FarmRole == PoultryFarm.Domain.Common.UserRole.SystemAdmin;
}

public sealed record LoginRequest(
    string Username,
    string Password,
    string? CompanyCode);

public sealed record ForcePasswordChangeRequest(
    string? CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public sealed record TwoFactorLoginRequest(
    string Username,
    string Password,
    string Code);

public sealed record LoginAttemptResult(
    bool IsBlocked,
    int RemainingAttempts);
