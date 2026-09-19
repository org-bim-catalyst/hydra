namespace AskLucy.Application.Abstractions;

/// <summary>
/// Background sends for the two account-recovery paths a signed-out visitor can reach from the
/// sign-in page: a fresh confirmation link when the first one was lost, and a message to the
/// support mailbox when an account has been locked out.
/// <para>
/// Both run out-of-band for the same reason <see cref="IPasswordResetIssuanceJob"/> does: the
/// endpoints answer 202 for every input, so no work whose duration depends on whether the
/// address has an account may happen on the request path (FR-003).
/// </para>
/// </summary>
public interface IAccountEmailJob
{
    /// <summary>
    /// Re-sends the account-confirmation link. Silently does nothing when the address has no
    /// account or that account is already confirmed — the caller has already answered 202 and
    /// must not learn which case applied.
    /// </summary>
    Task ResendConfirmationAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Relays a locked-out user's message to the support mailbox. The support address is
    /// configuration (<c>Smtp:FromSupport</c>) and never leaves the server, so the page can
    /// offer "contact an administrator" without publishing an address to harvest.
    /// </summary>
    Task SendAccountSupportRequestAsync(
        string fromEmail, string message, string? requestedFromIp, CancellationToken cancellationToken = default);
}
