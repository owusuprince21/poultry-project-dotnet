using System.Security.Claims;

namespace PoultryFarm.Api.Authorization;

public static class PermissionHelpers
{
    public static bool IsSystemAdmin(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var role = GetNormalizedRole(user);

        var isSystemAdminClaim = user.FindFirst("is_system_admin")?.Value;

        return role is AppRoles.SuperAdmin or AppRoles.SubAdmin or AppRoles.SystemAdmin ||
               string.Equals(isSystemAdminClaim, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var role = GetNormalizedRole(user);
        var isSuperuserClaim = user.FindFirst("is_superuser")?.Value;

        return role is AppRoles.SuperAdmin or AppRoles.SystemAdmin ||
               string.Equals(isSuperuserClaim, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCompanyAdmin(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var role = GetNormalizedRole(user);

        return IsSystemAdmin(user) || role == AppRoles.FarmAdmin;
    }

    public static bool IsWorker(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return GetNormalizedRole(user) == AppRoles.Worker;
    }

    public static string GetNormalizedRole(ClaimsPrincipal user)
    {
        var rawRole =
            user.FindFirst(ClaimTypes.Role)?.Value ??
            user.FindFirst("role")?.Value ??
            string.Empty;

        return AppRoles.Normalize(rawRole);
    }

    public static string? GetCompanyId(ClaimsPrincipal user)
    {
        return user.FindFirst("company_id")?.Value;
    }

    public static string? GetCompanyCode(ClaimsPrincipal user)
    {
        return user.FindFirst("company_code")?.Value;
    }
}
