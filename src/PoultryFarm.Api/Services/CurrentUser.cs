using System.Security.Claims;
using PoultryFarm.Application.Common.Interfaces;

namespace PoultryFarm.Api.Services;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? UserId => TryGetGuid(ClaimTypes.NameIdentifier);

    public Guid? CompanyId => TryGetGuid("company_id");

    public bool IsSystemAdmin =>
        User?.IsInRole("SystemAdmin") == true ||
        string.Equals(User?.FindFirst("is_system_admin")?.Value, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsCompanyAdmin => IsSystemAdmin || User?.IsInRole("Admin") == true;

    private Guid? TryGetGuid(string type)
    {
        var value = User?.FindFirst(type)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
