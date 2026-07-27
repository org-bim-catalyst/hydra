namespace AskLucy.Application.Users;

public sealed record UserProfileDto(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    DateOnly BirthDate,
    bool TwoFactorEnabled,
    string? AvatarFileName);
