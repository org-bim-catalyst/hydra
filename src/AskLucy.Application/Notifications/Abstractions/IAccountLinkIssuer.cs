using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// Mints the one-time link of an account email at send time (research R10, FR-009d). Nothing about
/// the link is stored by the hub: not on the notification, not on the delivery, not in the outbox, not
/// in the audit trail and not in a log. It goes only to the template renderer, which turns it into the
/// email's button.
/// </summary>
public interface IAccountLinkIssuer
{
    /// <summary>
    /// Issues a fresh link of <paramref name="kind"/> for <paramref name="userId"/>. The token's own
    /// record is persisted before this returns, so a link in a sent email always works (a crash between
    /// send and save can't leave a link the server doesn't know).
    /// </summary>
    /// <param name="targetAddress">The address the link is for: the new address for an email change, the recipient's own address otherwise.</param>
    /// <param name="isRetry">A later attempt at the same delivery: it is never throttled as if it were a new request.</param>
    /// <exception cref="AccountLinkRefusedException">The account may not be sent this link right now (not confirmed, locked out, throttled, already confirmed). The delivery is cancelled with the safe reason.</exception>
    Task<Uri> IssueAsync(SensitiveLinkKind kind, string userId, string targetAddress, bool isRetry, CancellationToken cancellationToken);
}

/// <summary>
/// An account email's link was not issued for a reason that is not a failure: the real reason is logged
/// to the security log, and the delivery is cancelled with <see cref="Exception.Message"/>, which is
/// safe to store and show (it never says whether an address has an account).
/// </summary>
public sealed class AccountLinkRefusedException(string safeReason) : Exception(safeReason);
