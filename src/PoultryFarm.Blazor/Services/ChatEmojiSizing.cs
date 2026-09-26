using System.Globalization;
using System.Text;

namespace PoultryFarm.Blazor.Services;

public static class ChatEmojiSizing
{
    private const int LargeEmojiMaxCount = 4;

    public static bool ShouldEnlarge(string? body) =>
        TryCountEmojiOnly(body, out var count) && count is >= 1 and <= LargeEmojiMaxCount;

    private static bool TryCountEmojiOnly(string? body, out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var enumerator = StringInfo.GetTextElementEnumerator(body.Trim());
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (string.IsNullOrWhiteSpace(element))
            {
                continue;
            }

            if (!IsEmojiElement(element))
            {
                count = 0;
                return false;
            }

            count++;
            if (count > LargeEmojiMaxCount)
            {
                return false;
            }
        }

        return count > 0;
    }

    private static bool IsEmojiElement(string element)
    {
        var hasEmojiCore = false;
        foreach (var rune in element.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || Rune.IsDigit(rune))
            {
                return false;
            }

            if (IsEmojiCore(rune.Value))
            {
                hasEmojiCore = true;
                continue;
            }

            if (IsEmojiModifier(rune.Value))
            {
                continue;
            }

            return false;
        }

        return hasEmojiCore;
    }

    private static bool IsEmojiCore(int value) =>
        value is (>= 0x1F300 and <= 0x1FAFF)
            or (>= 0x2600 and <= 0x27BF)
            or (>= 0x2300 and <= 0x23FF)
            or (>= 0x1F1E6 and <= 0x1F1FF)
            or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139
            or (>= 0x2194 and <= 0x2199)
            or 0x21A9 or 0x21AA
            or 0x24C2
            or 0x25AA or 0x25AB or 0x25B6 or 0x25C0
            or (>= 0x25FB and <= 0x25FE)
            or (>= 0x2B05 and <= 0x2B07)
            or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55
            or 0x3030 or 0x303D or 0x3297 or 0x3299;

    private static bool IsEmojiModifier(int value) =>
        value is 0x200D or 0x20E3 or 0xFE0F
            or (>= 0xFE00 and <= 0xFE0F)
            or (>= 0x1F3FB and <= 0x1F3FF)
            or (>= 0xE0020 and <= 0xE007F);
}
