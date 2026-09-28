using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/067 T098 — <see cref="DocumentNotification"/> moved onto the notification hub; the legacy
/// inbox, its endpoints and its read/list operations are gone (T097, T100), so this repository is
/// read-only now: the single method account deletion still needs. The entity, its configuration and
/// its table stay until the follow-up release's two-step drop (data-model.md § Migrations).
/// </summary>
public interface IDocumentNotificationRepository
{
    /// <summary>Purges a deleted user's legacy document notifications (T099); these rows have no FK to <c>ApplicationUser</c>, so nothing does this for free.</summary>
    Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default);
}
