namespace AskLucy.Application.Authorization;

/// <summary>
/// Claim type used to carry both a role's stored permission grants (in <c>AspNetRoleClaims</c>,
/// research.md Decision 1/1b) and a signed-in user's effective permissions (added by
/// <c>CurrentAuthorizationClaimsTransformation</c> to the authenticated principal).
/// </summary>
public static class PermissionClaims
{
    public const string Type = "permission";
}
