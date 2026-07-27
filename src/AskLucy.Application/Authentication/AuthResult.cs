namespace AskLucy.Application.Authentication;

public enum AuthOutcome
{
    Success,
    RequiresTwoFactor,
    InvalidCredentials,
    EmailNotConfirmed,
    LockedOut,
    Failed,
}

public sealed record AuthResult(
    AuthOutcome Outcome,
    string? UserId = null,
    string? AccessToken = null,
    DateTime? AccessTokenExpiresAtUtc = null,
    string? RefreshToken = null,
    IReadOnlyList<string>? Errors = null);
