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
    Task<IReadOnlyList<UserAdminDto>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<UserAdminDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(string userId, string? firstName, string? lastName, CancellationToken cancellationToken = default);
}
