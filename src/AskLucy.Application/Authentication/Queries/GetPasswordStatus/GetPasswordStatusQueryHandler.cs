using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Queries.GetPasswordStatus;

public sealed class GetPasswordStatusQueryHandler(IIdentityService identityService)
    : IRequestHandler<GetPasswordStatusQuery, bool>
{
    public Task<bool> Handle(GetPasswordStatusQuery request, CancellationToken cancellationToken) =>
        identityService.HasPasswordAsync(request.UserId, cancellationToken);
}
