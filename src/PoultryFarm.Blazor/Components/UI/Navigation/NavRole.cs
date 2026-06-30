namespace PoultryFarm.Blazor.Components.UI.Navigation;

public static class NavRole
{
    public const string SuperAdmin = "super_admin";
    public const string SubAdmin = "sub_admin";
    public const string SystemAdmin = "system_admin";
    public const string FarmAdmin = "farm_admin";
    public const string Worker = "worker";

    public static string Normalize(string? role)
    {
        var normalized = (role ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");

        return normalized switch
        {
            "superadmin" => SuperAdmin,
            "super_admin" => SuperAdmin,
            "superuser" => SuperAdmin,

            "subadmin" => SubAdmin,
            "sub_admin" => SubAdmin,

            "systemadmin" => SuperAdmin,
            "system_admin" => SystemAdmin,

            "farmadmin" => FarmAdmin,
            "farm_admin" => FarmAdmin,
            "companyadmin" => FarmAdmin,
            "company_admin" => FarmAdmin,
            "admin" => FarmAdmin,

            "farmworker" => Worker,
            "farm_worker" => Worker,
            "worker" => Worker,

            _ => normalized
        };
    }
}
