using AskLucy.Domain.Authorization;

namespace AskLucy.Application.Authorization;

/// <summary>
/// The built-in <c>User</c> role every account holds unless it's given another one: assigned at
/// sign-up, the role a user falls back to when theirs is deleted or removed, never deletable and
/// never renamable - no account is ever left with no role. It carries no Administrator or Super User
/// privilege. Its permissions can be added to, but never below <see cref="BaselinePermissionKeys"/>.
/// </summary>
public static class DefaultRole
{
    public const string Name = "User";

    public const string NormalizedName = "USER";

    /// <summary>
    /// The minimum a User-role holder needs to use the site, which can't be taken off the role. Empty
    /// today: every end-user feature (chat, knowledge bases, files, agents...) is gated on being signed
    /// in, not on a permission - the catalogue only holds administration permissions so far.
    /// </summary>
    public static readonly IReadOnlySet<string> BaselinePermissionKeys = new HashSet<string>(StringComparer.Ordinal);

    public static bool Is(string? roleName) => string.Equals(roleName, Name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The baseline plus whichever stored grants are still in the catalogue. A Super-User-controlled
    /// key is never honoured here: on the role everyone holds it would expose every user's content.
    /// </summary>
    public static PermissionSet Permissions(IEnumerable<string> storedKeys)
    {
        var keys = BaselinePermissionKeys
            .Concat(storedKeys.Where(key =>
                AdminPermissionCatalog.TryGet(key, out _) && !AdminPermissionCatalog.SuperUserControlledKeys.Contains(key)))
            .ToList();

        return keys.Count == 0 ? PermissionSet.Empty : PermissionSet.Create(keys);
    }
}
