namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// The only way a module requests a notification (FR-001, contracts/module-integration.md). It says
/// what happened, never how to deliver it: no channels, templates, HTML, URLs or secrets (FR-002).
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Adds a durable outbox event to the current unit of work. Performs no I/O and never throws for
    /// delivery reasons; it throws only for programming errors (unknown type, undeclared variable,
    /// malformed recipient), which surface in tests. The caller commits it with its own
    /// <c>IUnitOfWork.SaveChangesAsync</c> (research R2), so don't wrap the call in try/catch.
    /// </summary>
    void Publish(NotificationRequest request);
}

/// <param name="Type">A <c>NotificationTypeKeys</c> constant.</param>
/// <param name="Recipient">Who the notification is for.</param>
/// <param name="Variables">Declared variables only; plain values, never HTML or URLs.</param>
/// <param name="RelatedItem">Drives the action route and the access re-check (R24).</param>
/// <param name="EventKey">De-duplication identity (FR-008), e.g. <c>workflow-execution:{id}:failed</c>.</param>
/// <param name="Language">An explicit language, the first FR-044 candidate.</param>
public sealed record NotificationRequest(
    string Type,
    NotificationRecipient Recipient,
    IReadOnlyDictionary<string, string?> Variables,
    RelatedItem? RelatedItem = null,
    string? EventKey = null,
    string? Language = null);

/// <param name="Type">The related-item type an <see cref="INotificationAccessCheck"/> answers for.</param>
/// <param name="Id">Fills <c>{id}</c> in the type's route.</param>
/// <param name="ParentId">Fills <c>{parentId}</c> in nested routes (research R11 addendum).</param>
public sealed record RelatedItem(string Type, string Id, string? ParentId = null);

public abstract record NotificationRecipient
{
    /// <summary>The most user ids one <see cref="Users"/> request may carry.</summary>
    public const int MaxUsers = 100;

    public sealed record User(string UserId) : NotificationRecipient;

    public sealed record Users(IReadOnlyList<string> UserIds) : NotificationRecipient;

    /// <summary>Announcements only; expanded in batches at dispatch (R22).</summary>
    public sealed record Audience(bool AllActiveUsers, IReadOnlyList<string> RoleIds) : NotificationRecipient;

    /// <summary>An unverified or new address that belongs to a user (FR-009c).</summary>
    public sealed record AddressForUser(string UserId, string EmailAddress) : NotificationRecipient;

    /// <summary>Password reset: resolved in the background, so the response never reveals whether the address exists (R12).</summary>
    public sealed record AddressLookup(string EmailAddress) : NotificationRecipient;

    /// <summary>The address comes from server configuration only.</summary>
    public sealed record SupportMailbox : NotificationRecipient;
}
