using MediatR;

namespace AskLucy.Application.Authentication.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResult>;
