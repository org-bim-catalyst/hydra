using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Authorization;

/// <summary>API-facing projection of a <see cref="RoleRecord"/> (contracts/admin-roles-api.md §2).</summary>
public sealed record RoleSummaryDto(
    string Id, string Name, string? Description, bool IsBuiltIn,
    IReadOnlyList<string> PermissionKeys, int UserCount, DateTime? ModifiedAtUtc, string ConcurrencyStamp)
{
    public static RoleSummaryDto Create(RoleRecord record) => new(
        record.Id, record.Name, record.Description, record.IsBuiltIn,
        [.. record.Permissions.Keys], record.UserCount, record.ModifiedAtUtc, record.ConcurrencyStamp);
}
