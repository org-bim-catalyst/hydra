using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.ClearAiProviderCredential;

public sealed class ClearAiProviderCredentialCommandHandler(
    IAIProviderRepository providers,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<ClearAiProviderCredentialCommandHandler> logger) : IRequestHandler<ClearAiProviderCredentialCommand>
{
    public async Task Handle(ClearAiProviderCredentialCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");

        provider.ClearCredential(actorUserId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminAiProviderActionPerformed(
            logger, "ClearCredential", actorUserId, provider.Id, "Credential cleared, provider disabled");
    }
}
