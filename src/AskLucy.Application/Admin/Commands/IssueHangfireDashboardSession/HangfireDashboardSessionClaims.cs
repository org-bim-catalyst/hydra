namespace AskLucy.Application.Admin.Commands.IssueHangfireDashboardSession;

/// <summary>
/// Shared between the minting handler (writes the claim) and the Web layer's
/// <c>JwtBearerEvents.OnMessageReceived</c> callback (requires it) so the two never drift apart
/// — a token minted for any other purpose is rejected on <c>/hangfire</c> even if it is
/// otherwise a validly signed access token (specs/060-hangfire-dashboard-access, research.md
/// Decision 2).
/// </summary>
public static class HangfireDashboardSessionClaims
{
    public const string PurposeClaimType = "purpose";

    public const string PurposeClaimValue = "hangfire-dashboard";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
}
