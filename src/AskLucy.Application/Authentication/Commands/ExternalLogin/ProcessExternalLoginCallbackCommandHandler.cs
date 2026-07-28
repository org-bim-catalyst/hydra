using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

public sealed class ProcessExternalLoginCallbackCommandHandler(
    IIdentityService identityService, IExternalLoginCodeStore codeStore)
    : IRequestHandler<ProcessExternalLoginCallbackCommand, string?>
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

    public async Task<string?> Handle(ProcessExternalLoginCallbackCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.ResolveExternalLoginAsync(
            request.Provider, request.ProviderKey, request.Email, request.EmailVerified, request.LinkToUserId, cancellationToken);

        if (result.Status != IdentityResultStatus.Success || result.UserId is null)
        {
            return null;
        }

        return codeStore.Issue(result.UserId, CodeLifetime);
    }
}
