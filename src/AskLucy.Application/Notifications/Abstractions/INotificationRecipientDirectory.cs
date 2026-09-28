namespace AskLucy.Application.Notifications.Abstractions;

/// <param name="UserId">The account id.</param>
/// <param name="DisplayName">First and last name; null when the account has neither.</param>
/// <param name="Email">The account's current address.</param>
/// <param name="EmailConfirmed">Whether that address is verified; email goes only to verified addresses (FR-009c).</param>
/// <param name="IsActive">False for a deleted account; such a recipient gets nothing (R24).</param>
public sealed record NotificationRecipientInfo(
    string UserId,
    string? DisplayName,
    string? Email,
    bool EmailConfirmed,
    bool IsActive);

/// <summary>The account facts the dispatcher needs to route and render for a user.</summary>
public interface INotificationRecipientDirectory
{
    /// <summary>Every listed user that exists; unknown ids are omitted.</summary>
    Task<IReadOnlyDictionary<string, NotificationRecipientInfo>> GetAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}
