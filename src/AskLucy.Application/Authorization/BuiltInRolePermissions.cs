using AskLucy.Application.Users;
using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Authorization;

/// <summary>
/// The effective permissions of a built-in role — the one place both the resolver and the role
/// repository compute them. Built-in ⇒ the full catalogue (research.md Decision 2), with one
/// exception (specs/074 research D14): the Administrator role holds the
/// <see cref="AdminPermissionCatalog.SuperUserControlledKeys"/> only when a Super User stored the
/// grant on it. The default <see cref="DefaultRole"/> is built-in too but unprivileged: it holds only
/// its baseline plus what was added to it.
/// </summary>
public static class BuiltInRolePermissions
{
    public static PermissionSet For(string roleName, IEnumerable<string> storedKeys) =>
        string.Equals(roleName, PrivilegedRoleNames.Administrator, StringComparison.OrdinalIgnoreCase)
            ? Administrator(storedKeys)
            : DefaultRole.Is(roleName)
                ? DefaultRole.Permissions(storedKeys)
                : PermissionSet.Full;

    /// <summary>Full minus the controlled keys, plus whichever controlled keys are stored. Idempotent over its own output.</summary>
    public static PermissionSet Administrator(IEnumerable<string> storedKeys)
    {
        var baseline = PermissionSet.Full.Except(AdminPermissionCatalog.SuperUserControlledKeys);
        var grantedControlled = storedKeys.Where(AdminPermissionCatalog.SuperUserControlledKeys.Contains).ToList();

        return grantedControlled.Count == 0
            ? baseline
            : PermissionSet.Union(baseline, PermissionSet.Create(grantedControlled));
    }
}
