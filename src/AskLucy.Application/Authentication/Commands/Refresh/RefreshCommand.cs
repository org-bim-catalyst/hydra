using MediatR;

namespace AskLucy.Application.Authentication.Commands.Refresh;

public sealed record RefreshCommand(string RefreshToken) : IRequest<AuthResult>;
