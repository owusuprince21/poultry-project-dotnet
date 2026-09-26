namespace PoultryFarm.Blazor.Services;

public sealed record TableExportPayload(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? Subtitle = null);
