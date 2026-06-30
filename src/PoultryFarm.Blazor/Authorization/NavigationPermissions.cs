namespace PoultryFarm.Blazor.Authorization;

public static class NavigationPermissions
{
    public static bool CanAccessSystemAdmin(string? role)
    {
        return AppRoles.Normalize(role) == AppRoles.SystemAdmin;
    }

    public static bool CanAccessFarmAdmin(string? role)
    {
        var normalizedRole = AppRoles.Normalize(role);

        return normalizedRole is AppRoles.SystemAdmin or AppRoles.FarmAdmin;
    }

    public static bool CanAccessWorker(string? role)
    {
        var normalizedRole = AppRoles.Normalize(role);

        return normalizedRole is AppRoles.SystemAdmin or AppRoles.FarmAdmin or AppRoles.Worker;
    }

    public static bool CanManageCompanies(string? role)
    {
        return CanAccessSystemAdmin(role);
    }

    public static bool CanManageUsers(string? role)
    {
        return CanAccessSystemAdmin(role) || AppRoles.Normalize(role) == AppRoles.FarmAdmin;
    }

    public static bool CanWriteFarmOperations(string? role)
    {
        return CanAccessWorker(role);
    }

    public static bool CanManageSettings(string? role)
    {
        var normalizedRole = AppRoles.Normalize(role);

        return normalizedRole is AppRoles.SystemAdmin or AppRoles.FarmAdmin;
    }
}