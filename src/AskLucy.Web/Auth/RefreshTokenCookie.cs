namespace AskLucy.Web.Auth;

/// <summary>
/// Single source of truth for the httpOnly refresh-token cookie (name/path/options), so the
/// login/refresh/logout/session-check call sites in <see cref="Controllers.v1.AuthController"/>
/// can never drift out of sync with each other.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "askLucyRefreshToken";

    /// <summary>
    /// Scoped to the auth endpoints only, so the cookie is never attached to the far more
    /// numerous bearer-authenticated data calls.
    /// </summary>
    public const string Path = "/api/v1/auth";

    /// <summary>
    /// SameSite=None+Secure (not Lax): the Vite dev server and the API differ in scheme
    /// (http vs https), which browsers treat as cross-site under schemeful same-site — Lax
    /// would silently drop the cookie on credentialed fetch() in dev.
    /// </summary>
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
