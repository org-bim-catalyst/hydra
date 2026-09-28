namespace AskLucy.Domain.Notifications;

/// <summary>The default state of one channel for one type (data-model.md catalogue columns). A channel a type doesn't use is simply absent.</summary>
public enum ChannelDefault
{
    /// <summary>Optional, off unless the user turns the category on for this channel.</summary>
    Off,

    /// <summary>Optional, on unless the user turns the category off for this channel.</summary>
    On,

    /// <summary>Always sent; a preference can't turn it off (FR-032).</summary>
    Mandatory,
}

/// <summary>Account links minted at send time, never stored (research R10).</summary>
public enum SensitiveLinkKind
{
    EmailConfirmation,
    EmailChange,
    PasswordReset,
}

/// <summary>
/// A variable a type declares (FR-041). <see cref="Fallback"/> replaces a value that is missing at
/// render time, and the renderer logs a warning (spec edge case).
/// </summary>
public sealed record NotificationVariable(string Name, string Fallback);

/// <summary>
/// One catalogue row (research R7): what a type is, which channels it uses by default, and which
/// variables it may carry. Code-owned; administrators edit only templates.
/// </summary>
public sealed record NotificationTypeDefinition
{
    /// <summary>Available to every type (data-model.md "Standard variables").</summary>
    public static readonly IReadOnlyList<NotificationVariable> StandardVariables =
    [
        new("recipientDisplayName", "there"),
        new("appName", "Ask Lucy"),
        new("actionUrl", string.Empty),
        new("occurredAt", string.Empty),
    ];

    public required string Key { get; init; }

    public required NotificationCategory Category { get; init; }

    public required NotificationPriority DefaultPriority { get; init; }

    public required IReadOnlyDictionary<NotificationChannel, ChannelDefault> Channels { get; init; }

    /// <summary>Type-specific variables; <see cref="StandardVariables"/> are always allowed on top.</summary>
    public IReadOnlyList<NotificationVariable> DeclaredVariables { get; init; } = [];

    /// <summary>App-relative route with <c>{id}</c> and <c>{parentId}</c> tokens from the related item (R11 addendum). Null means no action link.</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>False for the email-only account types (FR-009c).</summary>
    public bool ShowInCenter { get; init; } = true;

    /// <summary>False for types defined ahead of their emitter (FR-005, research R27).</summary>
    public bool IsEmitted { get; init; } = true;

    /// <summary>Security types say only what happened and when (FR-025).</summary>
    public bool MinimizeSensitiveContent { get; init; }

    public SensitiveLinkKind? SensitiveLinkKind { get; init; }

    /// <summary>How long a queued request stays sendable before its delivery expires (R10).</summary>
    public TimeSpan? RequestValidity { get; init; }

    /// <summary>The dispatcher re-checks the recipient can still see the related item before rendering (R24).</summary>
    public bool RequiresItemAccess { get; init; }

    /// <summary>No type in this feature allows an external link (R11).</summary>
    public bool AllowsExternalLink { get; init; }

    /// <summary>Email goes out only for administrator-marked critical announcements (FR-004a).</summary>
    public bool EmailOnlyWhenCritical { get; init; }

    /// <summary>Approval notifications are audited on create, delivery and read (FR-037).</summary>
    public bool IsApproval { get; init; }

    /// <summary>The test-send type carries whatever variables the template under test declares.</summary>
    public bool VariablesFromTemplate { get; init; }

    public IEnumerable<NotificationChannel> SupportedChannels => Channels.Keys;

    public bool Supports(NotificationChannel channel) => Channels.ContainsKey(channel);

    public bool IsMandatory(NotificationChannel channel) =>
        Channels.TryGetValue(channel, out var state) && state == ChannelDefault.Mandatory;

    public bool IsEmailOnly => !Supports(NotificationChannel.InApp) && Supports(NotificationChannel.Email);

    /// <summary>Every variable a template for this type may reference.</summary>
    public IEnumerable<NotificationVariable> AllVariables => StandardVariables.Concat(DeclaredVariables);

    public bool Declares(string variableName) =>
        AllVariables.Any(v => string.Equals(v.Name, variableName, StringComparison.Ordinal));
}
