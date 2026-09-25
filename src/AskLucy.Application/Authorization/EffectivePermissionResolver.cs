using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Authorization;

/// <summary>
/// Built-in role ⇒ <see cref="PermissionSet.Full"/> (research.md Decision 2 — always the current
/// catalogue, never a stored snapshot), with one exception (specs/074 research D14): the built-in
/// Administrator role holds the <see cref="AdminPermissionCatalog.SuperUserControlledKeys"/> only
/// when a Super User stored the grant on it, so Administrators cost one role lookup. Custom role ⇒
/// its stored grants filtered to keys still in the catalogue (Decision 10 — a retired permission
/// silently drops out rather than erroring). No role ⇒ empty.
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

        // Avoids a repository round-trip for the role every Super User session already knows about.
        if (string.Equals(roleName, PrivilegedRoleNames.SuperUser, StringComparison.OrdinalIgnoreCase))
        {
            return PermissionSet.Full;
        }

        var role = await roleRepository.GetByNormalizedNameAsync(roleName.ToUpperInvariant(), cancellationToken);

        if (string.Equals(roleName, PrivilegedRoleNames.Administrator, StringComparison.OrdinalIgnoreCase))
        {
            return BuiltInRolePermissions.Administrator(role?.Permissions.Keys ?? []);
        }

        if (role is null)
        {
            return PermissionSet.Empty;
        }

        if (role.IsBuiltIn)
        {
            return BuiltInRolePermissions.For(role.Name, role.Permissions.Keys);
        }

        var liveKeys = role.Permissions.Keys.Where(key => AdminPermissionCatalog.TryGet(key, out _));
        return liveKeys.Any() ? PermissionSet.Create(liveKeys) : PermissionSet.Empty;
    }
}
