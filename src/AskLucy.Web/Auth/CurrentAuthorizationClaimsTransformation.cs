using System.Security.Claims;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authorization;
using AskLucy.Domain.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace AskLucy.Web.Auth;

/// <summary>
/// Replaces the JWT's baked-in <c>role</c> claims with the user's <b>current</b> role and adds a
/// <c>permission</c> claim per effective permission, on every request (research.md Decision 3).
/// Without this, a role change would only take effect after the 15-minute access token expires —
/// this closes that gap for every existing <c>IsInRole</c> caller too, not just the new
/// <see cref="PermissionAuthorizationHandler"/>. Cached per user for 30 seconds and evicted
/// immediately by <see cref="MemoryCacheAuthorizationCacheInvalidator"/> on any role change.
/// </summary>
public sealed class CurrentAuthorizationClaimsTransformation(
    IIdentityService identityService,
    IEffectivePermissionResolver permissionResolver,
    IMemoryCache cache) : IClaimsTransformation
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public static string CacheKey(string userId) => $"authz:{userId}";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return principal;
        }

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return principal;
        }

        // Only refresh claims for an identity we can actually resolve. A principal whose
        // subject doesn't correspond to a real Identity row (a synthetic/test token, or in
        // production a user deleted after their token was issued) keeps whatever claims it
        // arrived with — exactly today's pre-existing behavior — rather than being silently
        // stripped down to zero roles/permissions.
        var (roleName, permissions, resolvable) = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            if (!await identityService.UserExistsAsync(userId))
            {
                return (RoleName: (string?)null, Permissions: PermissionSet.Empty, Resolvable: false);
            }

            var roles = await identityService.GetRolesAsync(userId);
            var effective = await permissionResolver.ResolveAsync(userId);
            return (RoleName: roles.Count > 0 ? roles[0] : null, Permissions: effective, Resolvable: true);
        });

        if (!resolvable)
        {
            return principal;
        }

        foreach (var stale in identity.FindAll(ClaimTypes.Role).ToList())
        {
            identity.RemoveClaim(stale);
        }

        foreach (var stale in identity.FindAll(PermissionClaims.Type).ToList())
        {
            identity.RemoveClaim(stale);
        }

        if (roleName is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
        }

        foreach (var key in permissions.Keys)
        {
            identity.AddClaim(new Claim(PermissionClaims.Type, key));
        }

        return principal;
    }
}
