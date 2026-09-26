using System.Security.Claims;

namespace PoultryFarm.Api.Authorization;

public readonly record struct CompanyScope(Guid? CompanyId, bool Blocked);

public static class CompanyScopeResolver
{
    /// <summary>
    /// Resolves tenant scope. System admins may pass <paramref name="requestedCompany"/>.
    /// When <paramref name="requireCompanyForSystemAdmin"/> is true, missing company blocks the request
    /// (used for SuperAdmin read views that must pick a farm).
    /// </summary>
    public static CompanyScope Resolve(
        ClaimsPrincipal user,
        Guid? requestedCompany,
        Guid? userCompanyId,
        bool requireCompanyForSystemAdmin = false)
    {
        if (PermissionHelpers.IsSystemAdmin(user))
        {
            if (requireCompanyForSystemAdmin && !requestedCompany.HasValue)
            {
                return new CompanyScope(null, true);
            }

            return new CompanyScope(requestedCompany, false);
        }

        if (!userCompanyId.HasValue)
        {
            return new CompanyScope(null, true);
        }

        if (requestedCompany.HasValue && requestedCompany.Value != userCompanyId.Value)
        {
            return new CompanyScope(null, true);
        }

        return new CompanyScope(userCompanyId, false);
    }
}
