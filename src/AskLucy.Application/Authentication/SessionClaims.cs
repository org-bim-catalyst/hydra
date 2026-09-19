namespace AskLucy.Application.Authentication;

/// <summary>Claims that tie an access token back to the session (refresh-token family) it was issued for.</summary>
public static class SessionClaims
{
    /// <summary>
    /// The refresh-token family id. Standard JWT "session id" claim, so it survives the outbound
    /// short-name remapping in <c>TokenService</c> unchanged and reads idiomatically on the wire.
    /// </summary>
    public const string SessionId = "sid";
}
