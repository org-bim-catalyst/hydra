using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace AskLucy.Web.Auth;

/// <summary>
/// Refuses an access token whose session has been revoked, on the request that carries it.
///
/// A JWT is self-contained: revoking a refresh-token family stops the browser obtaining a
/// <i>new</i> access token, but the one it already holds stays valid and signed until it expires.
/// So "your other devices have been signed out" was true of the refresh cookie and false of the
/// bearer token — the other browser kept full API access for up to a whole access-token lifetime,
/// and only appeared to be signed out once a page reload forced it through <c>/auth/session</c>.
///
/// Mirrors <see cref="CurrentAuthorizationClaimsTransformation"/> deliberately: same 30-second
/// per-key cache as a safety net, same immediate eviction (<see cref="MemoryCacheSessionRevocationCache"/>)
/// so the very next request is the one that fails. The lookup is a single indexed EXISTS, and the
/// cache keeps it off the hot path for all but the first request in each window.
/// </summary>
public sealed partial class ActiveSessionTokenValidator(
    IRefreshTokenRepository refreshTokenRepository,
    IMemoryCache cache,
    ILogger<ActiveSessionTokenValidator> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public static string CacheKey(Guid tokenFamilyId) => $"session:{tokenFamilyId}";

    /// <summary>
    /// True when the token may proceed. A token with no session claim is allowed through: tokens
    /// minted before this check existed carry none, and failing them would sign every active user
    /// out on deploy. That legacy window closes on its own — such tokens are gone one access-token
    /// lifetime after rollout — so the case is logged rather than silently tolerated.
    /// </summary>
    public async Task<bool> IsStillActiveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var raw = principal.FindFirstValue(SessionClaims.SessionId) ?? principal.FindFirstValue(ClaimTypes.Sid);

        if (string.IsNullOrEmpty(raw))
        {
            LogMissingSessionClaim(logger, principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "(unknown)");
            return true;
        }

        if (!Guid.TryParse(raw, out var tokenFamilyId))
        {
            // Signed by us, so this is not tampering — but it is not a session id either, and we
            // cannot check what we cannot parse. Fail closed: the client still holds a valid
            // refresh cookie and will simply be issued a well-formed token.
            LogUnparseableSessionClaim(logger, raw);
            return false;
        }

        return await cache.GetOrCreateAsync(CacheKey(tokenFamilyId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await refreshTokenRepository.IsFamilyActiveAsync(tokenFamilyId, cancellationToken);
        });
    }

    [LoggerMessage(EventId = 5840, Level = LogLevel.Debug, Message = "Access token carries no session claim; allowed as a pre-rollout token. UserId={UserId}")]
    private static partial void LogMissingSessionClaim(ILogger logger, string userId);

    [LoggerMessage(EventId = 5841, Level = LogLevel.Warning, Message = "Security: access token rejected, session claim is not a valid id. Value={Value}")]
    private static partial void LogUnparseableSessionClaim(ILogger logger, string value);
}
