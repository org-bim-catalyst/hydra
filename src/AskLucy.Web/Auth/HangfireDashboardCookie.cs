namespace AskLucy.Web.Auth;

/// <summary>
/// Single source of truth for the httpOnly Hangfire-dashboard-session cookie (name/path/options),
/// mirroring <see cref="RefreshTokenCookie"/> (specs/060-hangfire-dashboard-access, research.md
/// Decision 2).
/// </summary>
public static class HangfireDashboardCookie
{
    public const string Name = "askLucyHangfireSession";

    /// <summary>
    /// Scoped to the dashboard itself only — never sent on any bearer-authenticated data call,
    /// so this cookie can never widen the attack surface of the rest of the app.
    /// </summary>
    public const string Path = "/hangfire";

    /// <summary>
    /// SameSite=Lax (not None, unlike <see cref="RefreshTokenCookie"/>/<see cref="AccessTokenCookie"/>):
    /// this cookie is only ever attached via a top-level browser navigation (the new tab opened
    /// by the admin panel), never via credentialed fetch(), so it doesn't need None's broader —
    /// and CSRF-riskier — reach. Lax still blocks the cookie on a cross-site forged POST against
    /// Hangfire's own job-mutating AJAX endpoints.
    /// </summary>
    public static CookieOptions BuildOptions(TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = Path,
        MaxAge = lifetime,
    };
}
