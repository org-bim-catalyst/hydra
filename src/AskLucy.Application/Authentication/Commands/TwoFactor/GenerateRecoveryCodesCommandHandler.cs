using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed class GenerateRecoveryCodesCommandHandler(IIdentityService identityService)
    : IRequestHandler<GenerateRecoveryCodesCommand, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GenerateRecoveryCodesCommand request, CancellationToken cancellationToken) =>
        identityService.GenerateRecoveryCodesAsync(request.UserId, cancellationToken);
}
