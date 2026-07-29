namespace AskLucy.Web.Contracts;

public sealed record UpdateProfileRequest(string? FirstName, string? LastName);

/// <summary>
/// Deliberately only these two fields — no id, passwordHash, role, etc. Anything else
/// in the client's request body is silently ignored by model binding, never persisted.
/// </summary>
public sealed record UpdateUserRequest(string? FirstName, string? LastName);

public sealed record AvatarUploadResponse(string AvatarUrl);

public sealed record DeleteAccountRequest(string Password);

/// <summary>specs/001-admin-dashboard FR-014. <c>Role</c> is <c>"Administrator"</c>, <c>"Super User"</c>, or the sentinel <c>"Regular"</c>.</summary>
public sealed record ChangeUserRoleRequest(string Role);
