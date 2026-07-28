using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

/// <summary>
/// Runs once the OAuth provider's callback has already produced a verified <see cref="Provider"/>/
/// <see cref="ProviderKey"/>/<see cref="Email"/> (from the provider's own claims, not client input —
/// see `WebAPI`'s `OnTicketReceived` handler). Resolves/creates/links the application user and
/// returns a one-time completion code the frontend exchanges via <see cref="CompleteExternalLoginCommand"/>,
/// or null if resolution failed.
/// </summary>
public sealed record ProcessExternalLoginCallbackCommand(
    string Provider, string ProviderKey, string? Email, bool EmailVerified, string? LinkToUserId) : IRequest<string?>;
