using MudBlazor;
using PoultryFarm.Domain.Marketplace;

namespace PoultryFarm.Blazor.Components.UI.Navigation;

public static class AppNavigation
{
    public static IReadOnlyCollection<NavGroup> Groups { get; } =
    [
        new NavGroup(
            "Overview",
            [
                new NavItem(
                    "Dashboard Overview",
                    "/dashboard",
                    Icons.Material.Filled.Dashboard,
                    NavPermission.AnyAuthenticated,
                    WorkerPageKeys.Dashboard),

                new NavItem(
                    "Farm Approvals",
                    "/farmer-registrations",
                    Icons.Material.Filled.HowToReg,
                    NavPermission.SystemAdminOnly,
                    ShowPendingFarmerBadge: true),

                new NavItem(
                    "Farms",
                    "/farms",
                    Icons.Material.Filled.Agriculture,
                    NavPermission.SystemAdminOnly),

                new NavItem(
                    "Audit Logs",
                    "/audit-logs",
                    Icons.Material.Filled.ManageSearch,
                    NavPermission.SystemAdminOnly),

                new NavItem(
                    "Users & Roles",
                    "/users",
                    Icons.Material.Filled.Groups,
                    NavPermission.SystemAdminOnly)
            ]),

        new NavGroup(
            "Platform Administration",
            [
                new NavItem(
                    "System Users",
                    "/system-users",
                    Icons.Material.Filled.AdminPanelSettings,
                    NavPermission.SystemAdminOnly)
            ]),

        new NavGroup(
            "Farm Administration",
            [
                new NavItem(
                    "Users & Roles",
                    "/users",
                    Icons.Material.Filled.Groups,
                    NavPermission.SubAdminOrFarmAdmin),

                new NavItem(
                    "Feed Configuration",
                    "/feed-configuration",
                    Icons.Material.Filled.Tune,
                    NavPermission.FarmAdminOnly)
            ]),

        new NavGroup(
            "Marketplace",
            [
                new NavItem(
                    "Listings",
                    "/marketplace/listings",
                    Icons.Material.Filled.Storefront,
                    NavPermission.FarmAdminOrWorker,
                    WorkerPageKeys.Marketplace),

                new NavItem(
                    "Activities",
                    "/marketplace/activities",
                    Icons.Material.Filled.Campaign,
                    NavPermission.FarmAdminOrWorker,
                    WorkerPageKeys.Marketplace),

                new NavItem(
                    "Inquiries",
                    "/marketplace/inquiries",
                    Icons.Material.Filled.ContactMail,
                    NavPermission.FarmAdminOrWorker,
                    WorkerPageKeys.Marketplace)
            ]),

        new NavGroup(
            "Operations",
            [
                new NavItem(
                    "Egg Production",
                    "/production",
                    Icons.Material.Filled.Egg,
                    NavPermission.OperationsAccess,
                    WorkerPageKeys.Production),

                new NavItem(
                    "Feed Management",
                    "/feed",
                    Icons.Material.Filled.Warehouse,
                    NavPermission.OperationsAccess,
                    WorkerPageKeys.Feed),

                new NavItem(
                    "Medication",
                    "/medication",
                    Icons.Material.Filled.Medication,
                    NavPermission.FarmAdminOnly),

                new NavItem(
                    "Batch Management",
                    "/batches",
                    Icons.Material.Filled.Inventory2,
                    NavPermission.FarmAdminOnly),

                new NavItem(
                    "Bird Health",
                    "/health",
                    Icons.Material.Filled.HealthAndSafety,
                    NavPermission.OperationsAccess,
                    WorkerPageKeys.Health),

                new NavItem(
                    "Debeaking",
                    "/debeaking",
                    Icons.Material.Filled.ContentCut,
                    NavPermission.FarmAdminOnly),

                new NavItem(
                    "Daily Summary Report",
                    "/daily-summary",
                    Icons.Material.Filled.Summarize,
                    NavPermission.FarmAdminOrWorker,
                    WorkerPageKeys.DailySummary)
            ]),

        new NavGroup(
            "Commercial",
            [
                new NavItem(
                    "Sales",
                    "/sales",
                    Icons.Material.Filled.PointOfSale,
                    NavPermission.CommercialAccess,
                    WorkerPageKeys.Sales)
            ]),

        new NavGroup(
            "Assistance",
            [
                new NavItem(
                    "Farm Assistance",
                    "/farm-assistance",
                    Icons.Material.Filled.SmartToy,
                    NavPermission.AssistanceAccess,
                    WorkerPageKeys.FarmAssistance)
            ]),

        new NavGroup(
            "Reports",
            [
                new NavItem(
                    "Reports",
                    "/reports",
                    Icons.Material.Filled.Assessment,
                    NavPermission.ReportsAccess)
            ]),

        new NavGroup(
            "Account",
            [
                new NavItem(
                    "User Profile & Settings",
                    "/settings",
                    Icons.Material.Filled.ManageAccounts,
                    NavPermission.AnyAuthenticated,
                    WorkerPageKeys.Settings)
            ])
    ];
}
