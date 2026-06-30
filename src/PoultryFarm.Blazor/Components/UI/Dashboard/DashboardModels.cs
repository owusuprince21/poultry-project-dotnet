using MudBlazor;

namespace PoultryFarm.Blazor.Components.UI.Dashboard;

public sealed record DashboardMetricItem(
    string Label,
    string Value,
    string Icon,
    Color Color,
    string? Detail = null);

public sealed record DashboardModuleItem(
    string Name,
    string Status,
    string Priority,
    Color Color);

public sealed record DashboardMessageItem(
    string Text,
    string Icon);

public sealed record SystemAdminDashboardOverview(
    DateTimeOffset GeneratedAt,
    IReadOnlyCollection<SystemAdminMetric> Metrics,
    IReadOnlyCollection<SystemAdminLineChart> Charts,
    IReadOnlyCollection<SystemAdminModule> Modules);

public sealed record SystemAdminMetric(
    string Label,
    string Value,
    string Detail,
    string Icon,
    string Color);

public sealed record SystemAdminLineChart(
    string Title,
    string Subtitle,
    IReadOnlyCollection<string> Labels,
    IReadOnlyCollection<SystemAdminChartSeries> Series);

public sealed record SystemAdminChartSeries(
    string Name,
    IReadOnlyCollection<decimal> Data);

public sealed record SystemAdminModule(
    string Name,
    string Status,
    string Priority,
    string Color);
