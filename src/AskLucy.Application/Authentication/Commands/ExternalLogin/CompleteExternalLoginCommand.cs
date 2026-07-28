using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

/// <summary>Exchanges a one-time completion code (from <see cref="ProcessExternalLoginCallbackCommand"/>) for real tokens.</summary>
public sealed record CompleteExternalLoginCommand(string Code) : IRequest<AuthResult>;
