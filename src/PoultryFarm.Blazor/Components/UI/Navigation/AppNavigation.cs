using MudBlazor;

namespace PoultryFarm.Blazor.Components.UI.Navigation;

public static class AppNavigation
{
    public static IReadOnlyCollection<NavGroup> Groups { get; } =
    [
        new NavGroup(
            "Dashboard",
            [
                new NavItem(
                    "Dashboard",
                    "/dashboard",
                    Icons.Material.Filled.Dashboard,
                    NavPermission.AnyAuthenticated)
            ]),

        new NavGroup(
            "Platform Administration",
            [
                new NavItem(
                    "Companies",
                    "/companies",
                    Icons.Material.Filled.Business,
                    NavPermission.SystemAdminOnly),

                new NavItem(
                    "Tenants",
                    "/tenants",
                    Icons.Material.Filled.Apartment,
                    NavPermission.SystemAdminOnly),

                new NavItem(
                    "System Users",
                    "/system-users",
                    Icons.Material.Filled.AdminPanelSettings,
                    NavPermission.SystemAdminOnly),

                new NavItem(
                    "Audit Logs",
                    "/audit-logs",
                    Icons.Material.Filled.ManageSearch,
                    NavPermission.SystemAdminOnly)
            ]),

        new NavGroup(
            "Farm Administration",
            [
                new NavItem(
                    "Users & Roles",
                    "/users",
                    Icons.Material.Filled.Groups,
                    NavPermission.SystemOrFarmAdmin),

                new NavItem(
                    "Feed Configuration",
                    "/feed-configuration",
                    Icons.Material.Filled.Tune,
                    NavPermission.FarmAdminOnly),

                new NavItem(
                    "Settings",
                    "/settings",
                    Icons.Material.Filled.Settings,
                    NavPermission.AnyAuthenticated)
            ]),

        new NavGroup(
            "Operations",
            [
                new NavItem(
                    "Egg Production",
                    "/production",
                    Icons.Material.Filled.Egg,
                    NavPermission.OperationsAccess),

                new NavItem(
                    "Feed Management",
                    "/feed",
                    Icons.Material.Filled.Warehouse,
                    NavPermission.OperationsAccess),

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
                    NavPermission.OperationsAccess),

                new NavItem(
                    "Debeaking",
                    "/debeaking",
                    Icons.Material.Filled.ContentCut,
                    NavPermission.FarmAdminOnly),

                new NavItem(
                    "Daily Summary Report",
                    "/daily-summary",
                    Icons.Material.Filled.Summarize,
                    NavPermission.WorkerOnly)
            ]),

        new NavGroup(
            "Commercial",
            [
                new NavItem(
                    "Sales",
                    "/sales",
                    Icons.Material.Filled.PointOfSale,
                    NavPermission.CommercialAccess)
            ]),

        new NavGroup(
            "Assistance",
            [
                new NavItem(
                    "Farm Assistance",
                    "/farm-assistance",
                    Icons.Material.Filled.SmartToy,
                    NavPermission.AssistanceAccess)
            ]),

        new NavGroup(
            "Reports",
            [
                new NavItem(
                    "Reports",
                    "/reports",
                    Icons.Material.Filled.Assessment,
                    NavPermission.ReportsAccess)
            ])
    ];
}
