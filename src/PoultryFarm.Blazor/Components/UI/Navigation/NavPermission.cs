namespace PoultryFarm.Blazor.Components.UI.Navigation;

public enum NavPermission
{
    AnyAuthenticated,
    SystemAdminOnly,
    FarmAdminOnly,
    FarmAdminOrWorker,
    WorkerOnly,
    SystemOrFarmAdmin,
    SubAdminOrFarmAdmin,
    OperationsAccess,
    CommercialAccess,
    AssistanceAccess,
    ReportsAccess,
    FarmAdminReports
}
