using MediatR;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed record DisableTwoFactorCommand(string UserId) : IRequest;
