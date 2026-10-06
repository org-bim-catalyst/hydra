using System.Globalization;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>
/// The emitter's variables plus the standard ones the hub fills (data-model.md "Standard variables"),
/// built the same way for the in-app render at materialization and the email render at send time.
/// </summary>
internal static class NotificationStandardVariables
{
    public static Dictionary<string, string?> Build(
        IReadOnlyDictionary<string, string?> variables,
        string? displayName,
        string? actionUrl,
        DateTime occurredAtUtc) =>
        new(variables, StringComparer.Ordinal)
        {
            ["recipientDisplayName"] = displayName,
            ["appName"] = null,
            ["actionUrl"] = actionUrl,
            ["occurredAt"] = occurredAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
        };
}
