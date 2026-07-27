using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

public sealed record ExternalLoginCommand(string Provider, string ProviderKey, string? Email) : IRequest<AuthResult>;
