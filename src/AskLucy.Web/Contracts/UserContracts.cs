namespace AskLucy.Web.Contracts;

public sealed record UpdateProfileRequest(string? FirstName, string? LastName);

/// <summary>
/// Deliberately only these two fields — no id, passwordHash, role, etc. Anything else
/// in the client's request body is silently ignored by model binding, never persisted.
/// </summary>
public sealed record UpdateUserRequest(string? FirstName, string? LastName);

public sealed record AvatarUploadResponse(string AvatarUrl);

/// <summary>
/// <see cref="AskLucy.Application.Users.UserProfileDto"/> plus a ready-to-use signed avatar URL —
/// every profile reader (Settings' Profile tab, the account menu, …) needs the same URL
/// <see cref="AskLucy.Web.Controllers.v1.UsersController.UploadAvatar"/> already hands back after
/// an upload, so it is computed once here rather than left for each caller to sign for itself.
/// </summary>
public sealed record MyProfileResponse(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    DateOnly BirthDate,
    bool TwoFactorEnabled,
    string? AvatarFileName,
    string? AvatarUrl);

public sealed record DeleteAccountRequest(string Password);

/// <summary>specs/001-admin-dashboard FR-014. <c>Role</c> is the user's role name - <c>"User"</c> for an account with no other role.</summary>
public sealed record ChangeUserRoleRequest(string Role);
