using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.SetVoiceProviderCredential;

public sealed class SetVoiceProviderCredentialCommandHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IAiCredentialProtector credentialProtector,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<SetVoiceProviderCredentialCommandHandler> logger) : IRequestHandler<SetVoiceProviderCredentialCommand, AdminVoiceProviderDto>
{
    public async Task<AdminVoiceProviderDto> Handle(SetVoiceProviderCredentialCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var all = await voiceProviders.ListByPriorityAsync(cancellationToken);
        var provider = all.FirstOrDefault(p => p.Id == request.ProviderId)
            ?? throw new KeyNotFoundException("Voice provider not found.");

        var engine = VoiceEngineResolution.GetEngine(engines, provider);
        if (!engine.RequiresCredential)
        {
            throw new DomainRuleViolationException($"{provider.DisplayName} runs on this server and does not use an API key.");
        }

        var apiKey = request.ApiKey.Trim();
        provider.SetCredential(credentialProtector.Protect(apiKey), CredentialHintFormatter.Format(apiKey), actorUserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminVoiceProviderActionPerformed(
            logger, "SetVoiceProviderCredential", actorUserId, provider.Id, "Credential set");

        return AdminVoiceProviderDto.FromEntity(provider, isPrimary: all[0].Id == provider.Id, engine.RequiresCredential);
    }
}
