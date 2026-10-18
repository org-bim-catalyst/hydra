namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// Hands every notification test its own ten-day window in 2020-2024, a range no run's "now" ever reaches. One allocator for the whole
/// process, so two tests (in one class or two) can't overlap: a random day per test collided now and then, and one test's leftover row
/// showed up as an extra bucket in another's.
/// </summary>
internal static class NotificationTestWindows
{
    private static readonly int FirstSlotDay = new Random().Next(0, 1000);
    private static int _nextSlot = -1;

    public static DateTime Next() =>
        new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(FirstSlotDay + (Interlocked.Increment(ref _nextSlot) * 10));
}
