using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;

public sealed class SetPrimaryVoiceProviderCommandHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<SetPrimaryVoiceProviderCommandHandler> logger) : IRequestHandler<SetPrimaryVoiceProviderCommand, IReadOnlyList<AdminVoiceProviderDto>>
{
    public async Task<IReadOnlyList<AdminVoiceProviderDto>> Handle(SetPrimaryVoiceProviderCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var all = await voiceProviders.ListByPriorityAsync(cancellationToken);
        var chosen = all.FirstOrDefault(p => p.Id == request.ProviderId)
            ?? throw new KeyNotFoundException("Voice provider not found.");

        // Fail here rather than let the router discover a missing engine mid-reply.
        VoiceEngineResolution.GetEngine(engines, chosen);

        chosen.SetDefaultVoice(request.VoiceId, actorUserId);

        // Re-number densely so priorities stay 0..n-1, with the chosen provider first and the
        // rest in the order the administrator already had them.
        List<VoiceProvider> ordered = [chosen, .. all.Where(p => p.Id != chosen.Id)];
        for (var priority = 0; priority < ordered.Count; priority++)
        {
            ordered[priority].SetPriority(priority, actorUserId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminVoiceProviderActionPerformed(
            logger, "SetPrimaryVoiceProvider", actorUserId, chosen.Id, $"Lucy's voice set to {chosen.ProviderKey}/{chosen.DefaultVoiceId}");

        return await VoiceEngineResolution.ToDtosAsync(engines, ordered, cancellationToken);
    }
}
