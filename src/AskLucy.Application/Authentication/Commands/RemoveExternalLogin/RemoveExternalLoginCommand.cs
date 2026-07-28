using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.RemoveExternalLogin;

public sealed record RemoveExternalLoginCommand(string UserId, string Provider, string ProviderKey) : IRequest<IdentityOperationResult>;
