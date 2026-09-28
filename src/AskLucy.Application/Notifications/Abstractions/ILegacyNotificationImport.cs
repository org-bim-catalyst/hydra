namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>How many legacy rows US9-A's one-time importer carried onto the hub this run.</summary>
public sealed record LegacyNotificationImportResult(int DocumentNotificationsImported, int MemoryNotificationsImported)
{
    public int Total => DocumentNotificationsImported + MemoryNotificationsImported;
}

/// <summary>
/// One-time replay of the pre-hub <c>DocumentNotification</c>/<c>MemoryNotification</c> rows onto
/// the hub's own <see cref="Domain.Notifications.Notification"/> table (specs/067 data-model.md
/// § Legacy mapping, T095). Idempotent — already-imported rows (identified by
/// <see cref="Domain.Notifications.Notification.EventKey"/>) are skipped on every subsequent run,
/// and the legacy rows themselves are never modified.
/// </summary>
public interface ILegacyNotificationImport
{
    Task<LegacyNotificationImportResult> ImportAsync(CancellationToken cancellationToken);
}
