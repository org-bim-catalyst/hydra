using AskLucy.Application.Abstractions;
using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Authorization;

/// <summary>
/// Built-in role ⇒ <see cref="PermissionSet.Full"/> (research.md Decision 2 — always the current
/// catalogue, never a stored snapshot). Custom role ⇒ its stored grants filtered to keys still in
/// the catalogue (Decision 10 — a retired permission silently drops out rather than erroring).
/// No role ⇒ empty.
/// </summary>
public sealed class EffectivePermissionResolver(IIdentityService identityService, IRoleRepository roleRepository) : IEffectivePermissionResolver
{
    public async Task<PermissionSet> ResolveAsync(string userId, CancellationToken cancellationToken = default)
    {
        var roles = await identityService.GetRolesAsync(userId, cancellationToken);
        var roleName = roles.Count > 0 ? roles[0] : null;
        if (roleName is null)
        {
            return PermissionSet.Empty;
        }

        if (PrivilegedRoleIsBuiltIn(roleName))
        {
            return PermissionSet.Full;
        }

        var role = await roleRepository.GetByNormalizedNameAsync(roleName.ToUpperInvariant(), cancellationToken);
        if (role is null)
        {
            return PermissionSet.Empty;
        }

        if (role.IsBuiltIn)
        {
            return PermissionSet.Full;
        }

        var liveKeys = role.Permissions.Keys.Where(key => AdminPermissionCatalog.TryGet(key, out _));
        return liveKeys.Any() ? PermissionSet.Create(liveKeys) : PermissionSet.Empty;
    }

    // Avoids a repository round-trip for the two names every session already knows about.
    private static bool PrivilegedRoleIsBuiltIn(string roleName) =>
        string.Equals(roleName, "Administrator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(roleName, "Super User", StringComparison.OrdinalIgnoreCase);
}
