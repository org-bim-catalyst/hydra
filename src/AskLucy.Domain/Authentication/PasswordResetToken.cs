namespace AskLucy.Domain.Authentication;

/// <summary>
/// A single-use, time-limited right to set a new password for one account
/// (specs/058-password-recovery). Only the SHA-256 hash of the token is ever persisted, so
/// read access to storage cannot reconstruct a usable reset link (FR-016).
/// <para>
/// This entity exists instead of ASP.NET Identity's stateless <c>DataProtectorTokenProvider</c>
/// reset tokens because those cannot be superseded by a newer request, are not single-use in
/// their own right, and put account recovery on the Data Protection key ring's survival — see
/// research.md Topic 1.
/// </para>
/// Like its <see cref="RefreshToken"/> sibling it deliberately does not derive from
/// <c>BaseEntity</c>/soft-delete: consumed and superseded rows are audit-relevant and are kept.
/// </summary>
public sealed class PasswordResetToken
{
    public Guid Id { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// The account's email address at the moment the token was issued. A mismatch at redemption
    /// rejects the token, so changing the account's email invalidates every outstanding link
    /// (FR-006).
    /// </summary>
    public string EmailAtIssue { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public DateTime? SupersededAtUtc { get; private set; }

    /// <summary>Client origin the request came from, for the audit trail (FR-015).</summary>
    public string? RequestedFromIp { get; private set; }

    public bool IsRedeemable =>
        ConsumedAtUtc is null && SupersededAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;

    private PasswordResetToken()
    {
        // Required by EF Core materialization.
    }

    /// <summary>
    /// Creates a pending token. <paramref name="lifetime"/> is explicit — as on
    /// <see cref="RefreshToken.IssueNew"/> — so tests can construct an already-expired token by
    /// passing a negative value, without a clock abstraction or a slow test.
    /// </summary>
    public static PasswordResetToken IssueNew(
        string userId,
        string tokenHash,
        string emailAtIssue,
        TimeSpan lifetime,
        string? requestedFromIp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emailAtIssue);

        if (tokenHash is not { Length: 64 } || !tokenHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Token hash must be a 64-character hex SHA-256 digest.", nameof(tokenHash));
        }

        var now = DateTime.UtcNow;

        return new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            EmailAtIssue = emailAtIssue,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime),
            RequestedFromIp = requestedFromIp,
        };
    }

    /// <summary>Marks the token spent. Idempotent, so a double-submit cannot reset twice.</summary>
    public void Consume() => ConsumedAtUtc ??= DateTime.UtcNow;

    /// <summary>Invalidates the token because a newer one was issued, or the password changed.</summary>
    public void Supersede() => SupersededAtUtc ??= DateTime.UtcNow;

    /// <summary>
    /// True when this token may set a password for <paramref name="currentEmail"/>. Combines the
    /// lifecycle check with the email binding so callers cannot accidentally check only one.
    /// </summary>
    public bool CanRedeemFor(string currentEmail) =>
        IsRedeemable && string.Equals(EmailAtIssue, currentEmail, StringComparison.OrdinalIgnoreCase);
}
