namespace PoultryFarm.Blazor.Services;

public static class IdleTimeoutPreferences
{
    public const int DefaultMinutes = 5;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 240;

    public static event Func<Task>? Changed;

    public static string StorageKey(string? userId) =>
        $"poultryfarm.idleTimeoutMinutes.{(string.IsNullOrWhiteSpace(userId) ? "anon" : userId)}";

    public static int ClampMinutes(int minutes) =>
        Math.Clamp(minutes, MinMinutes, MaxMinutes);

    public static int ToMilliseconds(int minutes) =>
        ClampMinutes(minutes) * 60_000;

    public static async Task RaiseChangedAsync()
    {
        if (Changed is null)
        {
            return;
        }

        await Changed.Invoke();
    }
}
