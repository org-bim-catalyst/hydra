using MediatR;

namespace AskLucy.Application.Authentication.Commands.LoginTwoFactor;

public sealed record LoginTwoFactorCommand(string UserId, string Code, bool IsRecoveryCode) : IRequest<AuthResult>;
