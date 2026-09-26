namespace PoultryFarm.Domain.Marketplace;

public static class FarmActivityReactionEmojis
{
    public static readonly string[] Allowed =
    [
        "👍",
        "❤️",
        "😂",
        "😮",
        "😢",
        "🔥"
    ];

    public static bool IsAllowed(string? emoji) =>
        !string.IsNullOrWhiteSpace(emoji) && Allowed.Contains(emoji);
}
