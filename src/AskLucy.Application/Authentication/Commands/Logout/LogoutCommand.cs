using MediatR;

namespace AskLucy.Application.Authentication.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken) : IRequest;
