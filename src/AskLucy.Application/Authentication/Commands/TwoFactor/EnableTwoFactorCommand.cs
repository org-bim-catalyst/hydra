using MediatR;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

/// <summary>Returns the TOTP authenticator key for the caller to enroll (FR-011).</summary>
public sealed record EnableTwoFactorCommand(string UserId) : IRequest<string>;
