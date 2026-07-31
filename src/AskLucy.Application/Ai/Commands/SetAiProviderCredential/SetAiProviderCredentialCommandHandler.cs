using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.SetAiProviderCredential;

public sealed class SetAiProviderCredentialCommandHandler(
    IAIProviderRepository providers,
    IAiCredentialProtector credentialProtector,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<SetAiProviderCredentialCommandHandler> logger) : IRequestHandler<SetAiProviderCredentialCommand>
{
    public async Task Handle(SetAiProviderCredentialCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");

        var ciphertext = credentialProtector.Protect(request.ApiKey);
        provider.SetCredential(ciphertext, actorUserId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminAiProviderActionPerformed(
            logger, "SetCredential", actorUserId, provider.Id, "Credential set");
    }
}
