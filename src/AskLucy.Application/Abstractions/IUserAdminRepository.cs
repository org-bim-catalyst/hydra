using AskLucy.Application.Users;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Admin-facing user access. Every read returns <see cref="UserAdminDto"/> — never the
/// raw Identity entity (FR-019) — and the only write path is
/// <see cref="UpdateAsync"/>'s explicit, allow-listed field set (closes the legacy
/// overposting/mass-assignment vulnerability, spec.md &#167; Gap Analysis).
/// </summary>
public interface IUserAdminRepository
{
    Task<UserAdminDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(string userId, string? firstName, string? lastName, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a user (FR-016) — sets <c>IsDeleted</c>/<c>DeletedAtUtc</c>/<c>DeletedBy</c>, never a hard delete.</summary>
    Task<bool> DeleteAsync(string userId, string actorUserId, CancellationToken cancellationToken = default);

    /// <summary>FR-009/010/011 — partial name/email search, single-column sort, offset pagination. <paramref name="sortBy"/> is <c>"email"</c> or <c>"createdAtUtc"</c>.</summary>
    Task<PagedResult<UserAdminDto>> SearchAsync(
        string? search, string sortBy, bool sortDescending, int page, int pageSize, CancellationToken cancellationToken = default);
}
