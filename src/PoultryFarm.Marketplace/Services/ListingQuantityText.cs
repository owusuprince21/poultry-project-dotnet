namespace PoultryFarm.Marketplace.Services;

public static class ListingQuantityText
{
    public static string Available(decimal? quantity, string? unitLabel)
    {
        if (quantity is not decimal qty || qty <= 0)
        {
            return "Quantity unknown";
        }

        var unit = string.IsNullOrWhiteSpace(unitLabel) ? "units" : unitLabel.Trim();
        var label = qty == 1 || unit.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            ? unit
            : $"{unit}s";
        return $"{qty:N0} {label} available";
    }

    public static string Amount(decimal? quantity, string? unitLabel)
    {
        if (quantity is not decimal qty || qty <= 0)
        {
            return "Unknown";
        }

        var unit = string.IsNullOrWhiteSpace(unitLabel) ? "units" : unitLabel.Trim();
        var label = qty == 1 || unit.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            ? unit
            : $"{unit}s";
        return $"{qty:N0} {label}";
    }
}
