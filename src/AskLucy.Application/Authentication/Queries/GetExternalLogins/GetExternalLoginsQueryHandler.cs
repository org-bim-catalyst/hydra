using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Queries.GetExternalLogins;

public sealed class GetExternalLoginsQueryHandler(IIdentityService identityService)
    : IRequestHandler<GetExternalLoginsQuery, IReadOnlyList<ExternalLoginDto>>
{
    public Task<IReadOnlyList<ExternalLoginDto>> Handle(GetExternalLoginsQuery request, CancellationToken cancellationToken) =>
        identityService.GetExternalLoginsAsync(request.UserId, cancellationToken);
}
