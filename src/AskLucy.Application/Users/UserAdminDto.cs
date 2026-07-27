namespace AskLucy.Application.Users;

/// <summary>
/// Admin-facing user projection. Deliberately excludes every Identity secret
/// (password hash, security stamp, concurrency stamp) — closes the legacy exposure
/// where the raw <c>ApplicationUser</c> entity was serialized directly (FR-019).
/// </summary>
public sealed record UserAdminDto(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    bool LockoutEnabled);
