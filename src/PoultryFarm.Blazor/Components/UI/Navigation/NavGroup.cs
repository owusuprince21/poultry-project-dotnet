namespace PoultryFarm.Blazor.Components.UI.Navigation;

public sealed record NavGroup(
    string Title,
    IReadOnlyCollection<NavItem> Items);