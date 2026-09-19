namespace AskLucy.Web.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? FirstName, string? LastName);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginTwoFactorRequest(string UserId, string Code, bool IsRecoveryCode);

public sealed record ExternalLoginCompleteRequest(string Code);

public sealed record AuthResponse(string? UserId, string? AccessToken, DateTime? ExpiresAtUtc, bool RequiresTwoFactor);

/// <summary>specs/055-role-management: <c>Permissions</c> is the caller's effective permission-catalogue keys, resolved fresh every request (research.md Decision 3).</summary>
public sealed record SessionResponse(bool Authenticated, string? UserId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record ConfirmEmailRequest(string UserId, string Token);

public sealed record ChangePasswordRequest(string? CurrentPassword, string NewPassword);

/// <summary>Password recovery (specs/058-password-recovery).</summary>
public sealed record ForgotPasswordRequest(string Email);

public sealed record ResendEmailConfirmationRequest(string Email);

public sealed record AccountSupportRequest(string Email, string Message);

public sealed record ResetPasswordRequest(string UserId, string Token, string NewPassword);

public sealed record ValidateResetTokenRequest(string UserId, string Token);

public sealed record PasswordStatusResponse(bool HasPassword);

public sealed record RequestEmailChangeRequest(string NewEmail);

public sealed record ConfirmEmailChangeRequest(string UserId, string NewEmail, string Token);

public sealed record ExternalLoginResponse(string Provider, string ProviderKey, string DisplayName);

public sealed record OperationResultResponse(bool Succeeded, IReadOnlyList<string>? Errors);
