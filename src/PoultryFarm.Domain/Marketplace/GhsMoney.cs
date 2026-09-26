using System.Globalization;

namespace PoultryFarm.Domain.Marketplace;

public static class GhsMoney
{
    public static string Format(decimal amount) =>
        string.Create(CultureInfo.InvariantCulture, $"GH₵{amount:0.00}");

    public static string? FormatOrNull(decimal? amount) =>
        amount is null ? null : Format(amount.Value);
}
