namespace AskLucy.Application.Abstractions;

/// <summary>
/// Background delivery of the two password-flow emails (specs/058-password-recovery). Enqueued
/// via <c>IBackgroundJobClient</c> against this interface, never the concrete type, mirroring
/// <see cref="IMemoryExtractionJob"/>.
/// <para>
/// Dispatch is deliberately off the request path: awaiting an SMTP round trip inside
/// <c>/auth/password/forgot</c> would make response time depend on whether the address has an
/// account, which is the enumeration oracle FR-003 exists to close (research.md Topic 3).
/// </para>
/// </summary>
public interface IPasswordEmailJob
{
    /// <summary>
    /// Sends the reset link. <paramref name="protectedToken"/> MUST be the ciphertext produced by
    /// <see cref="IPasswordTokenProtector.Protect"/>, never the plaintext token: Hangfire
    /// serializes job arguments into its SQL job store, and a plaintext token there would put a
    /// usable reset link in the same database that deliberately stores only hashes
    /// (FR-016, research.md Topic 5a).
    /// </summary>
    Task SendResetLinkAsync(string userId, string email, string protectedToken, CancellationToken cancellationToken = default);

    /// <summary>Tells the account holder their password changed, and what to do if it was not them (FR-011).</summary>
    Task SendPasswordChangedNoticeAsync(string email, DateTime changedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Narrow wrapper over ASP.NET Core Data Protection, so Application can keep a reset token out of
/// the Hangfire job store without referencing <c>IDataProtector</c> itself (constitution §3).
/// </summary>
public interface IPasswordTokenProtector
{
    string Protect(string plaintextToken);

    /// <summary>
    /// Returns null when the ciphertext cannot be unprotected — normally a key ring rotated while
    /// the job sat queued. The caller MUST fail loudly rather than mail a broken link.
    /// </summary>
    string? Unprotect(string protectedToken);
}
