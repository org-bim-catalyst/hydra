using AskLucy.Application.Abstractions;
using Hangfire;
using MediatR;

namespace AskLucy.Application.Authentication.Commands.ExternalLogin;

public sealed class ProcessExternalLoginCallbackCommandHandler(
    IIdentityService identityService,
    IExternalLoginCodeStore codeStore,
    IUserProfileRepository userProfileRepository,
    IBackgroundJobClient backgroundJobClient)
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

        // Name sync stays synchronous (research.md Decision 3) — merge-on-null so a claim the
        // provider didn't supply on this sign-in never clobbers a previously known value.
        var currentProfile = await userProfileRepository.GetByIdAsync(result.UserId, cancellationToken);
        var effectiveFirstName = request.FirstName ?? currentProfile?.FirstName;
        var effectiveLastName = request.LastName ?? currentProfile?.LastName;
        await userProfileRepository.UpdateAsync(result.UserId, effectiveFirstName, effectiveLastName, cancellationToken);

        // Picture sync is asynchronous (research.md Decision 4) — a slow/failing fetch must never
        // add latency to, or fail, sign-in itself.
        if (request.PictureUrl is not null)
        {
            backgroundJobClient.Enqueue<IExternalProfilePictureSyncJob>(
                j => j.SyncAsync(result.UserId, request.PictureUrl, CancellationToken.None));
        }

        return codeStore.Issue(result.UserId, CodeLifetime);
    }
}
