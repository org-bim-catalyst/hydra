namespace AskLucy.WebAPI.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? FirstName, string? LastName);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginTwoFactorRequest(string UserId, string Code, bool IsRecoveryCode);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ExternalLoginRequest(string Provider, string ProviderKey, string? Email);

public sealed record AuthResponse(string? UserId, string? AccessToken, DateTime? ExpiresAtUtc, string? RefreshToken, bool RequiresTwoFactor);
