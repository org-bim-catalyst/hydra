using MediatR;

namespace AskLucy.Application.Authentication.Commands.ChangeEmail;

public sealed record RequestEmailChangeCommand(string UserId, string NewEmail) : IRequest;
