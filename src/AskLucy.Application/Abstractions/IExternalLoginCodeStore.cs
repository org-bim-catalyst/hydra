namespace AskLucy.Application.Abstractions;

/// <summary>
/// Short-lived, single-use opaque tokens that bridge the OAuth browser-redirect flow (which
/// cannot carry an Authorization header) back to an authenticated user id, without ever
/// putting a JWT, refresh token, or other long-lived credential in a URL. Used both for the
/// login-completion code (issued after a provider callback resolves a user) and the
/// link-initiation ticket (issued before redirecting the browser to link an additional
/// provider to the current user, per FR-034).
/// </summary>
public interface IExternalLoginCodeStore
{
    /// <summary>Issues a new single-use code bound to <paramref name="userId"/>, valid for <paramref name="lifetime"/>.</summary>
    string Issue(string userId, TimeSpan lifetime);

    /// <summary>Consumes <paramref name="code"/> exactly once; returns the bound user id, or null if unknown/expired/already used.</summary>
    string? TryConsume(string code);
}
