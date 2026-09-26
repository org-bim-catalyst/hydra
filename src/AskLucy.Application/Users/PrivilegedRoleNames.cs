namespace AskLucy.Application.Users;

/// <summary>The two privileged roles (existing, unchanged — FR-014/FR-023 reason about them by name, no new roles are introduced).</summary>
public static class PrivilegedRoleNames
{
    public const string Administrator = "Administrator";
    public const string SuperUser = "Super User";

    /// <summary>
    /// Legacy name for "no privileged role", still accepted by <c>PATCH /users/{userId}/role</c>. Every
    /// account now holds a real role, so it resolves to the built-in User role
    /// (<c>Authorization.DefaultRole</c>) - never a real <c>AspNetRoles</c> row of its own.
    /// </summary>
    public const string Regular = "Regular";

    public static readonly string[] All = [Administrator, SuperUser];
}
