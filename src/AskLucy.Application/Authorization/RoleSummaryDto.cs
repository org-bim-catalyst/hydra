using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Authorization;

/// <summary>API-facing projection of a <see cref="RoleRecord"/> (contracts/admin-roles-api.md §2).</summary>
public sealed record RoleSummaryDto(
    string Id, string Name, string? Description, bool IsBuiltIn,
    IReadOnlyList<string> PermissionKeys, int UserCount, DateTime? ModifiedAtUtc, string ConcurrencyStamp)
{
    /// <summary>The built-in User role: can't be deleted or renamed, and its permissions can only be added to.</summary>
    public bool IsDefault { get; init; }

    /// <summary>What can't be taken off the role - <see cref="DefaultRole.BaselinePermissionKeys"/> for the User role, none otherwise.</summary>
    public IReadOnlyList<string> LockedPermissionKeys { get; init; } = [];

    public static RoleSummaryDto Create(RoleRecord record) => new(
        record.Id, record.Name, record.Description, record.IsBuiltIn,
        [.. record.Permissions.Keys], record.UserCount, record.ModifiedAtUtc, record.ConcurrencyStamp)
    {
        IsDefault = record.IsDefault,
        LockedPermissionKeys = record.IsDefault ? [.. DefaultRole.BaselinePermissionKeys] : [],
    };
}
