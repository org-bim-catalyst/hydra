using System.Security.Claims;

namespace AskLucy.Application.Abstractions;

public enum IdentityResultStatus
{
    Success,
    InvalidCredentials,
    RequiresTwoFactor,
    EmailNotConfirmed,
    LockedOut,
    Failed,
}

public sealed record IdentityOperationResult(
    IdentityResultStatus Status,
    string? UserId = null,
    IReadOnlyList<Claim>? Claims = null,
    IReadOnlyList<string>? Errors = null);

/// <summary>
/// Wraps ASP.NET Core Identity's <c>UserManager</c>/<c>SignInManager</c> so Application
/// never references the Persistence-owned <c>ApplicationUser</c> type or ASP.NET Identity
/// result types directly (constitution &#167;3/&#167;5, Dependency Inversion). Implemented in
/// <c>AskLucy.Persistence</c>, which is where <c>ApplicationUser</c> and the Identity
/// store already live.
/// </summary>
public interface IIdentityService
{
    Task<IdentityOperationResult> RegisterAsync(string email, string password, string? firstName, string? lastName, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ValidateTwoFactorCodeAsync(string userId, string code, bool isRecoveryCode, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ValidateExternalLoginAsync(string provider, string providerKey, string? email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Claim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default);

    Task<string> EnableTwoFactorAsync(string userId, CancellationToken cancellationToken = default);

    Task DisableTwoFactorAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);

    Task<string> GenerateEmailConfirmationTokenAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);
}
