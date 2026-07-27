namespace AskLucy.WebAPI.Contracts;

public sealed record UpdateProfileRequest(string? FirstName, string? LastName);

/// <summary>
/// Deliberately only these two fields — no id, passwordHash, role, etc. Anything else
/// in the client's request body is silently ignored by model binding, never persisted.
/// </summary>
public sealed record UpdateUserRequest(string? FirstName, string? LastName);

public sealed record AvatarUploadResponse(string AvatarUrl);
