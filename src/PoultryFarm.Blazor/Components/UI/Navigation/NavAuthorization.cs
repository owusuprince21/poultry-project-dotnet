using PoultryFarm.Blazor.Services;
using PoultryFarm.Domain.Marketplace;

namespace PoultryFarm.Blazor.Components.UI.Navigation;

public static class NavAuthorization
{
    public static bool IsVisible(AuthSession authSession, NavItem item)
    {
        if (!IsVisible(authSession, item.Permission))
        {
            return false;
        }

        if (!authSession.IsWorker)
        {
            return true;
        }

        var pageKey = item.PageKey ?? WorkerPageKeys.FromHref(item.Href);
        if (!WorkerPageKeys.Assignable.Contains(pageKey))
        {
            return true;
        }

        return authSession.CanAccessPage(pageKey);
    }

    public static bool IsVisible(AuthSession authSession, NavPermission permission)
    {
        if (!authSession.IsAuthenticated)
        {
            return false;
        }

        var role = NavRole.Normalize(authSession.Role);

        var isSuperAdmin = role is NavRole.SuperAdmin or NavRole.SystemAdmin;
        var isSubAdmin = role == NavRole.SubAdmin;
        var isSystemAdmin = isSuperAdmin || isSubAdmin;
        var isFarmAdmin = role == NavRole.FarmAdmin;
        var isWorker = role == NavRole.Worker;

        return permission switch
        {
            NavPermission.AnyAuthenticated => true,
            NavPermission.SystemAdminOnly => isSuperAdmin,
            NavPermission.FarmAdminOnly => isFarmAdmin,
            NavPermission.FarmAdminOrWorker => isFarmAdmin || isWorker,
            NavPermission.WorkerOnly => isWorker,
            NavPermission.SystemOrFarmAdmin => isSystemAdmin || isFarmAdmin,
            NavPermission.SubAdminOrFarmAdmin => isSubAdmin || isFarmAdmin,
            NavPermission.OperationsAccess => isSystemAdmin || isFarmAdmin || isWorker,
            NavPermission.CommercialAccess => isSystemAdmin || isFarmAdmin || isWorker,
            NavPermission.AssistanceAccess => isFarmAdmin || isWorker,
            NavPermission.ReportsAccess => isSuperAdmin || isFarmAdmin,
            NavPermission.FarmAdminReports => isFarmAdmin,
            _ => false
        };
    }
}
