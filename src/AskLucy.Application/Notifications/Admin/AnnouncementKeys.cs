namespace AskLucy.Application.Notifications.Admin;

/// <summary>How an announcement and the notifications it fans out to are tied together.</summary>
public static class AnnouncementKeys
{
    /// <summary>The related-item type on the event and on every notification the announcement creates.</summary>
    public const string RelatedItemType = "SystemAnnouncement";

    /// <summary>The event key the fan-out is published under: one event per announcement.</summary>
    public static string EventKey(Guid announcementId) => $"announcement:{announcementId}";
}
