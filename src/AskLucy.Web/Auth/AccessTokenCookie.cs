namespace AskLucy.Web.Auth;

/// <summary>
/// Delivers the short-lived JWT access token to SignalR hub connections via cookie instead of
/// the client's <c>accessTokenFactory</c>. A JS-managed factory closes over the token at
/// connection-setup time and never re-reads it, so once it expires every future negotiate/
/// reconnect on that connection is permanently rejected — a cookie is attached fresh by the
/// browser on every request instead, so there's no stale closure to go bad. REST calls are
/// unaffected: they keep using the `Authorization: Bearer` header from the client's in-memory
/// token, set from the auth response body as before.
/// </summary>
public static class AccessTokenCookie
{
    public const string Name = "askLucyAccessToken";

    /// <summary>
    /// Site-wide, unlike <see cref="RefreshTokenCookie"/>: set from /api/v1/auth/* responses
    /// but must also reach /hubs/* negotiate and WebSocket-upgrade requests.
    /// </summary>
    public const string Path = "/";

    public static CookieOptions BuildOptions(TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = Path,
        MaxAge = lifetime,
    };

    public static CookieOptions DeleteOptions => new()
    {
        Path = Path,
        Secure = true,
        SameSite = SameSiteMode.None,
    };
}
