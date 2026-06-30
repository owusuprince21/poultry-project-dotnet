namespace PoultryFarm.Application.Common.Models;

public sealed record PagedResult<T>(
    IReadOnlyCollection<T> Items,
    int TotalCount,
    int Page,
    int PageSize);
