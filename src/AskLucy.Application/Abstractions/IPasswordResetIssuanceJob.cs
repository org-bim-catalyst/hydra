namespace AskLucy.Application.Abstractions;

/// <summary>
/// Resolves an address to an account, decides whether it may have a reset link, issues the token
/// and hands the email off — all on a Hangfire worker (specs/058-password-recovery US1).
/// <para>
/// This exists as a job, rather than as work the request thread does, because of FR-003. Every
/// step here is account-dependent: an address with an account costs an eligibility read, a throttle
/// count, a supersede sweep and a save, while an address without one costs a single lookup. On a
/// remote database that difference is over a second of response time — an enumeration oracle a
/// neutral 202 body does nothing to hide (measured by <c>ForgotPasswordEndpointTests</c>). Keeping
/// the request path free of it is what makes the four account states indistinguishable.
/// </para>
/// Enqueued via <c>IBackgroundJobClient</c> against this interface, never the concrete type, so
/// Hangfire resolves it through the container — the same idiom as <see cref="IMemoryExtractionJob"/>.
/// </summary>
public interface IPasswordResetIssuanceJob
{
    /// <summary>
    /// <paramref name="email"/> is whatever the user typed; it may belong to no account at all,
    /// which is a normal, successful outcome recorded in the security log.
    /// </summary>
    Task IssueAsync(string email, string? requestedFromIp, CancellationToken cancellationToken = default);
}
