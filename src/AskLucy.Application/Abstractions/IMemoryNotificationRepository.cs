using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/067 T098 — <see cref="MemoryNotification"/> moved onto the notification hub; the legacy
/// inbox, its endpoints and its read/list operations are gone (T097, T101), so this repository is
/// read-only now: the single method account deletion still needs. Unlike <c>MemoryAuditLog</c>,
/// these rows carry no audit obligation, so deletion (not anonymization) is correct here. The
/// entity, its configuration and its table stay until the follow-up release's two-step drop
/// (data-model.md § Migrations).
/// </summary>
public interface IMemoryNotificationRepository
{
    Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default);
}
