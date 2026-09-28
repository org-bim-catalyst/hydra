using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Authorization;

/// <summary>
/// Centralizes the "does this notification belong to this caller" check (contracts/notifications-api.md:
/// "A notification id that belongs to another user... returns 404"). A plain static guard rather than
/// an ASP.NET Core <c>IAuthorizationHandler</c> — those types live in
/// <c>Microsoft.AspNetCore.Authorization</c>, which Application must not reference (constitution §3,
/// Dependency Rule). Mirrors <c>DocumentOwnershipGuard</c>. Owner-deletion is invisible here for free:
/// <see cref="Notification"/>'s EF query filter excludes soft-deleted rows before this ever runs.
/// </summary>
public static class NotificationOwnershipGuard
{
    public static Notification EnsureOwnedBy(Notification? notification, string userId)
    {
        if (notification is null || !string.Equals(notification.RecipientUserId, userId, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        return notification;
    }
}
