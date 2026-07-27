using MediatR;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed record GenerateRecoveryCodesCommand(string UserId) : IRequest<IReadOnlyList<string>>;
