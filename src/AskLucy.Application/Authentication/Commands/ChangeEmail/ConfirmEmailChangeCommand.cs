using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

public sealed record ConfirmEmailChangeCommand(string UserId, string NewEmail, string Token) : IRequest<IdentityOperationResult>;
