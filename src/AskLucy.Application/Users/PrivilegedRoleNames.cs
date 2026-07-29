namespace AskLucy.Application.Users;

/// <summary>The two privileged roles (existing, unchanged — FR-014/FR-023 reason about them by name, no new roles are introduced).</summary>
public static class PrivilegedRoleNames
{
    public const string Administrator = "Administrator";
    public const string SuperUser = "Super User";

    /// <summary>The sentinel meaning "no privileged role" — never a real <c>AspNetRoles</c> row.</summary>
    public const string Regular = "Regular";

    public static readonly string[] All = [Administrator, SuperUser];
}
