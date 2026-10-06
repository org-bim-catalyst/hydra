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

    /// <summary>
    /// The account whose address is <paramref name="email"/> (compared the way sign-in does), or null when none
    /// has it. A deleted account is returned inactive, so the caller treats it as "no recipient" without being
    /// able to tell the two apart from the outside (FR-009e).
    /// </summary>
    Task<NotificationRecipientInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="take"/> active accounts in id order, starting after <paramref name="afterUserId"/> (null: from the start),
    /// limited to accounts holding any of <paramref name="roleIds"/> when given. The id order is what lets a fan-out resume from a cursor.
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveUserIdsAfterAsync(
        IReadOnlyCollection<string>? roleIds, string? afterUserId, int take, CancellationToken cancellationToken);

    /// <summary>How many active accounts an audience reaches (<paramref name="roleIds"/> null: everyone), optionally only those with a verified address.</summary>
    Task<int> CountActiveAsync(IReadOnlyCollection<string>? roleIds, bool verifiedEmailOnly, CancellationToken cancellationToken);
}
