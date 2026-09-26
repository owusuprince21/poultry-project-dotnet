namespace PoultryFarm.Domain.Marketplace;

public static class WorkerPageKeys
{
    public const string Dashboard = "dashboard";
    public const string Production = "production";
    public const string Feed = "feed";
    public const string Health = "health";
    public const string Sales = "sales";
    public const string Marketplace = "marketplace";
    public const string DailySummary = "daily-summary";
    public const string FarmAssistance = "farm-assistance";
    public const string Settings = "settings";

    public static readonly IReadOnlyList<string> Assignable =
    [
        Dashboard,
        Production,
        Feed,
        Health,
        Sales,
        Marketplace,
        DailySummary,
        FarmAssistance,
        Settings
    ];

    public static string FromHref(string href) =>
        href.Trim().TrimStart('/').ToLowerInvariant();
}
