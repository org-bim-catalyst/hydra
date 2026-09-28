namespace AskLucy.Application.Options;

/// <summary>
/// Bound from configuration (constitution §4). Tuning for the notification hub (specs/067): outbox
/// dispatch, retry schedules, the email send limiter, retention and health thresholds. Every
/// property has a default and nothing is validated on start, so a missing section never stops the
/// host; out-of-range values are clamped where they are read.
/// </summary>
public sealed class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public NotificationDispatchOptions Dispatch { get; init; } = new();

    public NotificationRetryOptions Retry { get; init; } = new();

    public NotificationEmailOptions Email { get; init; } = new();

    public NotificationRetentionOptions Retention { get; init; } = new();

    public NotificationChannelsOptions Channels { get; init; } = new();

    public NotificationCenterOptions Center { get; init; } = new();

    public NotificationHealthCheckOptions HealthChecks { get; init; } = new();
}

/// <summary>Research R3/R4 — outbox dispatcher and delivery worker polling and claiming.</summary>
public sealed class NotificationDispatchOptions
{
    public int PollIntervalSeconds { get; init; } = 1;

    public int IdlePollIntervalSeconds { get; init; } = 5;

    public int BatchSize { get; init; } = 50;

    /// <summary>Well above the 60 s SMTP timeout, so a live send never loses its lease (R4).</summary>
    public int LeaseMinutes { get; init; } = 2;
}

/// <summary>Research R6 — per-priority retry schedules. Attempt n waits the n-th delay; the last delay repeats.</summary>
public sealed class NotificationRetryOptions
{
    private static readonly int[] DefaultDelaysMinutes = [1, 4, 10, 20, 30];
    private static readonly int[] DefaultCriticalDelaysSeconds = [30, 60, 120, 300, 600];

    public int MaxAttempts { get; init; } = 5;

    // Empty by default: the configuration binder appends to a non-empty array, so a default here
    // plus the appsettings values would bind as ten delays. The Effective* properties fall back.
    public int[] DelaysMinutes { get; init; } = [];

    public int[] CriticalDelaysSeconds { get; init; } = [];

    public IReadOnlyList<int> EffectiveDelaysMinutes => DelaysMinutes.Length > 0 ? DelaysMinutes : DefaultDelaysMinutes;

    public IReadOnlyList<int> EffectiveCriticalDelaysSeconds => CriticalDelaysSeconds.Length > 0 ? CriticalDelaysSeconds : DefaultCriticalDelaysSeconds;
}

/// <summary>Research R6 — the email send limiter and its reserved lane for mandatory mail.</summary>
public sealed class NotificationEmailOptions
{
    public int MaxPerMinute { get; init; } = 60;

    /// <summary>Capacity bulk traffic can never consume, so a password reset is never queued behind an announcement.</summary>
    public int ReservedPerMinuteForMandatory { get; init; } = 20;

    public int SendTimeoutSeconds { get; init; } = 60;
}

/// <summary>Research R20 — retention windows. Audit rows are never deleted.</summary>
public sealed class NotificationRetentionOptions
{
    public int ReadDays { get; init; } = 90;

    public int DeletedDays { get; init; } = 30;

    public int FailedDays { get; init; } = 30;

    public int DeliveryDays { get; init; } = 90;

    public int CompletedOutboxDays { get; init; } = 7;

    public int BatchSize { get; init; } = 1000;
}

public sealed class NotificationChannelsOptions
{
    public NotificationChannelToggle InApp { get; init; } = new();

    public NotificationChannelToggle Email { get; init; } = new();
}

public sealed class NotificationChannelToggle
{
    public bool Enabled { get; init; } = true;
}

/// <summary>Research R19 — notification center paging.</summary>
public sealed class NotificationCenterOptions
{
    public int DefaultPageSize { get; init; } = 25;

    public int MaxPageSize { get; init; } = 100;
}

/// <summary>Research R21 — thresholds for the <c>notifications-*</c> readiness checks.</summary>
public sealed class NotificationHealthCheckOptions
{
    public int HeartbeatStaleSeconds { get; init; } = 30;

    public int BacklogDegradedMinutes { get; init; } = 5;

    public int BacklogUnhealthyMinutes { get; init; } = 30;

    public int SmtpProbeCacheMinutes { get; init; } = 5;
}
