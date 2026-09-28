using System.Text.RegularExpressions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Notifications;

/// <summary>
/// Fills a type's route template from the related item (research R11). The template is code-owned,
/// and every substituted value is URL-encoded, so the result is always a path on this app: a
/// template that would leave the app is rejected rather than linked to.
/// </summary>
public sealed partial class NotificationLinkBuilder(IOptions<AppOptions> appOptions, ILogger<NotificationLinkBuilder> logger)
    : INotificationLinkBuilder
{
    [GeneratedRegex(@"\{(?<token>[a-zA-Z]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public string? BuildRelative(NotificationTypeDefinition definition, RelatedItem? relatedItem, Guid notificationId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.RouteTemplate is not { } template)
        {
            return null;
        }

        string? missing = null;
        var route = TokenRegex().Replace(template, match =>
        {
            var token = match.Groups["token"].Value;
            var value = token switch
            {
                "id" => relatedItem?.Id,
                "parentId" => relatedItem?.ParentId,
                "notificationId" => notificationId.ToString(),
                _ => throw new InvalidOperationException($"Route template '{template}' of '{definition.Key}' uses unknown token '{{{token}}}'."),
            };

            if (string.IsNullOrEmpty(value))
            {
                missing ??= token;
                return string.Empty;
            }

            return Uri.EscapeDataString(value);
        });

        if (missing is not null)
        {
            // The notification is still worth showing without its action link.
            NotificationLinkLog.RouteUnfilled(logger, definition.Key, missing, relatedItem?.Type);
            return null;
        }

        if (!definition.AllowsExternalLink && !IsAppRelative(route))
        {
            throw new InvalidOperationException($"Route template '{template}' of '{definition.Key}' doesn't produce an app-relative path.");
        }

        return route;
    }

    public string? BuildAbsolute(NotificationTypeDefinition definition, RelatedItem? relatedItem, Guid notificationId)
    {
        var route = BuildRelative(definition, relatedItem, notificationId);
        if (route is null || !IsAppRelative(route))
        {
            return route;
        }

        return appOptions.Value.FrontendBaseUrl.TrimEnd('/') + route;
    }

    /// <summary>A single leading slash: <c>//host</c> and <c>/\host</c> are protocol-relative to browsers.</summary>
    private static bool IsAppRelative(string route) =>
        route.Length > 0
        && route[0] == '/'
        && (route.Length == 1 || (route[1] != '/' && route[1] != '\\'))
        && !route.Contains("://", StringComparison.Ordinal);
}

internal static partial class NotificationLinkLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Notification type {Type} has no action link: its route needs '{Token}', which related item type {RelatedItemType} didn't provide.")]
    public static partial void RouteUnfilled(ILogger logger, string type, string token, string? relatedItemType);
}
