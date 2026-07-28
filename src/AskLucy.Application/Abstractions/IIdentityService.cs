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

    /// <summary>
    /// Resolves an external (Google/Facebook) identity into an application user. The caller
    /// MUST have already verified <paramref name="provider"/>/<paramref name="providerKey"/>/
    /// <paramref name="email"/> against the provider itself (e.g. via ASP.NET Core's OAuth
    /// handler completing the real authorization-code exchange) — this method never re-verifies
    /// them and trusts them as given, so it must never be reachable with client-supplied,
    /// unverified values (that was T073's vulnerability: the previous implementation accepted
    /// these three values directly from an anonymous POST body).
    /// </summary>
    /// <param name="emailVerified">
    /// Whether the provider itself asserts <paramref name="email"/> is verified. Only used to
    /// auto-link/auto-register by email during first-time sign-in (<paramref name="linkToUserId"/>
    /// is null) — an unverified email is never used to resolve an existing account.
    /// </param>
    /// <param name="linkToUserId">
    /// When set, links the external login to this already-authenticated user (FR-034) instead
    /// of resolving/creating an account by email.
    /// </param>
    Task<IdentityOperationResult> ResolveExternalLoginAsync(
        string provider, string providerKey, string? email, bool emailVerified, string? linkToUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Claim>> GetClaimsAsync(string userId, CancellationToken cancellationToken = default);

    Task<string> EnableTwoFactorAsync(string userId, CancellationToken cancellationToken = default);

    Task DisableTwoFactorAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);

    Task<string> GenerateEmailConfirmationTokenAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task<bool> VerifyPasswordAsync(string userId, string password, CancellationToken cancellationToken = default);

    Task<bool> HasPasswordAsync(string userId, CancellationToken cancellationToken = default);

    Task<string> GenerateChangeEmailTokenAsync(string userId, string newEmail, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> ChangeEmailAsync(string userId, string newEmail, string token, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalLoginDto>> GetExternalLoginsAsync(string userId, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> RemoveExternalLoginAsync(string userId, string provider, string providerKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record ExternalLoginDto(string Provider, string ProviderKey, string DisplayName);
