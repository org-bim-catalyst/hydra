namespace AskLucy.Web.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? FirstName, string? LastName);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginTwoFactorRequest(string UserId, string Code, bool IsRecoveryCode);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ExternalLoginCompleteRequest(string Code);

public sealed record AuthResponse(string? UserId, string? AccessToken, DateTime? ExpiresAtUtc, string? RefreshToken, bool RequiresTwoFactor);

public sealed record ConfirmEmailRequest(string UserId, string Token);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record RequestEmailChangeRequest(string NewEmail);

public sealed record ConfirmEmailChangeRequest(string UserId, string NewEmail, string Token);

public sealed record ExternalLoginResponse(string Provider, string ProviderKey, string DisplayName);

public sealed record OperationResultResponse(bool Succeeded, IReadOnlyList<string>? Errors);
