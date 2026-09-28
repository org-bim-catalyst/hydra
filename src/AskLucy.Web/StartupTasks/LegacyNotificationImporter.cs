using AskLucy.Application.Notifications.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Web.StartupTasks;

/// <summary>
/// One-time replay of the pre-hub <c>DocumentNotification</c>/<c>MemoryNotification</c> rows onto
/// the notification hub (specs/067 data-model.md § Legacy mapping, T096). Invoked unconditionally
/// from Program.cs, like <see cref="CredentialHintBackfillService"/> — production has the real
/// legacy rows to migrate, not just dev/test data — and wrapped the same way: idempotent (see
/// <see cref="ILegacyNotificationImport"/>), so a missing/unreachable database at startup degrades
/// to a logged warning rather than a crashed host, and repeated runs are a cheap no-op once the
/// backlog is imported.
/// </summary>
public static class LegacyNotificationImporter
{
    public static async Task RunAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<ILegacyNotificationImport>();

        var result = await importer.ImportAsync(CancellationToken.None);

#pragma warning disable CA1848, CA1873
        logger.LogInformation(
            "Legacy notification import: {DocumentCount} document, {MemoryCount} memory notification(s) imported.",
            result.DocumentNotificationsImported, result.MemoryNotificationsImported);
#pragma warning restore CA1848, CA1873
    }
}
