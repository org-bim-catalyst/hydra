using MediatR;

namespace AskLucy.Application.Authentication.Commands.ConfirmEmail;

public sealed record ConfirmEmailCommand(string UserId, string Token) : IRequest<bool>;
