namespace PoultryFarm.Api.Authorization;

public static class AppPolicies
{
    public const string SystemAdminOnly = "SystemAdminOnly";
    public const string CompanyAdminOnly = "CompanyAdminOnly";
    public const string AdminOrWorkerLimited = "AdminOrWorkerLimited";
}