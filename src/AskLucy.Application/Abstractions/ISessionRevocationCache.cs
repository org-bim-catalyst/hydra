namespace AskLucy.Application.Abstractions;

/// <summary>
/// Evicts a cached "is this session still live?" answer so a revoked session is refused on its very
/// next request rather than waiting out the short safety-net expiry.
///
/// Access tokens are self-contained JWTs: revoking a refresh-token family stops the browser getting
/// a <i>new</i> access token, but the one it already holds stays cryptographically valid until it
/// expires. Signing other devices out therefore has to be checked, not just recorded — see
/// <c>ActiveSessionTokenValidator</c>.
/// </summary>
public interface ISessionRevocationCache
{
    void Evict(Guid tokenFamilyId);
}
