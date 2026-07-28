using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Users.Commands.DeleteMyAccount;

public sealed record DeleteMyAccountCommand(string Password) : IRequest<IdentityOperationResult>;
