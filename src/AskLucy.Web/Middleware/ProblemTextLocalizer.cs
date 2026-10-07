using System.Globalization;
using AskLucy.Application.Localization;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Templates;

namespace AskLucy.Web.Middleware;

/// <summary>
/// Localizes a Problem Details <c>title</c> and <c>detail</c> for a localized surface (research R15). The key is the problem type's last
/// segment: <c>Problem.{slug}.Title</c>, then <c>Problem.{slug}.Detail</c>, or <c>.Detail.{Reason}</c> for the exceptions whose text depends on a
/// machine-readable reason. A detail built from the request (a token name, a field) has no entry and stays as the server wrote it.
/// <c>traceId</c>, <c>reason</c> and error codes are never touched (FR-046b).
/// </summary>
internal static class ProblemTextLocalizer
{
    public static (string Title, string Detail) Localize(string type, string title, string detail, Exception exception, CultureInfo culture)
    {
        var slug = type[(type.LastIndexOf('/') + 1)..];
        var reason = exception switch
        {
            DeliveryRetryRefusedException retry => retry.Reason.ToString(),
            NotificationTemplateConflictException conflict => conflict.Reason.ToString(),
            _ => null,
        };

        var localizedTitle = LocalizedMessages.Get($"Problem.{slug}.Title", culture) ?? title;
        var localizedDetail =
            (reason is null ? null : LocalizedMessages.Get($"Problem.{slug}.Detail.{reason}", culture))
            ?? LocalizedMessages.Get($"Problem.{slug}.Detail", culture)
            ?? detail;
        return (localizedTitle, localizedDetail);
    }
}
