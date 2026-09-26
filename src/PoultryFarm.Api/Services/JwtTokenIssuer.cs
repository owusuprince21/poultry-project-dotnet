using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Services;

public sealed class JwtTokenIssuer(IConfiguration configuration, ApplicationDbContext dbContext)
{
    public async Task<string> CreateTokenAsync(ApplicationUser user, Company? company = null, CancellationToken cancellationToken = default)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? "development-signing-key-change-before-production-12345";

        var issuer = configuration["Jwt:Issuer"] ?? "PoultryFarm.Api";
        var audience = configuration["Jwt:Audience"] ?? "PoultryFarm.Client";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var normalizedRole = NormalizeRole(user);
        var isSuperAdmin = user.IsSystemAdmin && user.FarmRole != UserRole.SubAdmin;

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

        if (user.FarmRole == UserRole.Worker && !user.IsSystemAdmin)
        {
            var pages = await dbContext.WorkerPagePermissions
                .AsNoTracking()
                .Where(x => x.UserId == user.Id)
                .Select(x => x.PageKey)
                .ToListAsync(cancellationToken);

            foreach (var page in pages)
            {
                claims.Add(new Claim("allowed_page", page));
            }
        }

        if (user.FarmRole == UserRole.MarketplaceBuyer)
        {
            claims.Add(new Claim("marketplace_buyer", "true"));
        }

        var lifetimeHours = user.FarmRole == UserRole.MarketplaceBuyer ? 24 : 8;
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(lifetimeHours),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NormalizeRole(ApplicationUser user)
    {
        if (user.IsSystemAdmin)
        {
            return user.FarmRole == UserRole.SubAdmin ? AppRoles.SubAdmin : AppRoles.SuperAdmin;
        }

        return user.FarmRole switch
        {
            UserRole.Admin => AppRoles.FarmAdmin,
            UserRole.Worker => AppRoles.Worker,
            UserRole.MarketplaceBuyer => AppRoles.MarketplaceBuyer,
            UserRole.SubAdmin => AppRoles.SubAdmin,
            _ => AppRoles.Normalize(user.FarmRole.ToString())
        };
    }
}
