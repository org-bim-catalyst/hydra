namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// The address support requests go to, from server configuration only (FR-009c). It is never stored on a
/// delivery and never shown in any API response; the delivery worker reads it at send time.
/// </summary>
public interface ISupportMailboxResolver
{
    /// <summary>Null when no support mailbox is configured; the delivery then fails visibly.</summary>
    string? GetAddress();
}
