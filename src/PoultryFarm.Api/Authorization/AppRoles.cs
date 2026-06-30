namespace PoultryFarm.Api.Authorization;

public static class AppRoles
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

            "systemadmin" => SystemAdmin,
            "system_admin" => SystemAdmin,

            "admin" => FarmAdmin,
            "farmadmin" => FarmAdmin,
            "farm_admin" => FarmAdmin,
            "company_admin" => FarmAdmin,

            "farmworker" => Worker,
            "farm_worker" => Worker,
            "worker" => Worker,

            _ => normalized
        };
    }
}
