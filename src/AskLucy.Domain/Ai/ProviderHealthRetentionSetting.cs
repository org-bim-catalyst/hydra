using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// specs/082 — the platform-wide number of days provider health-check history is kept. Exactly one row, keyed by
/// <see cref="SingletonId"/>; the migration seeds it and the daily retention job falls back to configuration when it
/// is missing.
/// </summary>
public sealed class ProviderHealthRetentionSetting : BaseEntity
{
    public static readonly Guid SingletonId = new("3a9f0b21-6c4d-4e87-b5a2-082000000001");

    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 365;
    public const int DefaultRetentionDays = 30;

    /// <summary>Days of health-check history kept before the daily job deletes older rows.</summary>
    public int RetentionDays { get; private set; }

    private ProviderHealthRetentionSetting()
    {
        // Required by EF Core materialization.
    }

    public static bool IsValidRetentionDays(int value) => value is >= MinRetentionDays and <= MaxRetentionDays;

    /// <summary>
    /// Builds the singleton row for tests and seeding scenarios. The application never writes it directly: the migration
    /// seeds the row and every later change goes through <see cref="Update"/>.
    /// </summary>
    public static ProviderHealthRetentionSetting Create(int retentionDays, string actor, DateTime utcNow)
    {
        var setting = new ProviderHealthRetentionSetting
        {
            Id = SingletonId,
            CreatedAtUtc = utcNow,
            CreatedBy = actor,
        };
        setting.Update(retentionDays, actor, utcNow);
        return setting;
    }

    /// <summary>
    /// Replaces the window. Throws <see cref="ArgumentOutOfRangeException"/> for a value outside its range, changing
    /// nothing.
    /// </summary>
    public void Update(int retentionDays, string actor, DateTime utcNow)
    {
        if (!IsValidRetentionDays(retentionDays))
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                retentionDays,
                $"The retention window must be between {MinRetentionDays} and {MaxRetentionDays} days.");
        }

        RetentionDays = retentionDays;
        ModifiedAtUtc = utcNow;
        ModifiedBy = actor;
    }
}
