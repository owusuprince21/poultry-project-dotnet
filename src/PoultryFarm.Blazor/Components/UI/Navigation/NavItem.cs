namespace PoultryFarm.Blazor.Components.UI.Navigation;

public sealed record NavItem(
    string Title,
    string Href,
    string Icon,
    NavPermission Permission,
    string? PageKey = null,
    bool ShowPendingFarmerBadge = false);
