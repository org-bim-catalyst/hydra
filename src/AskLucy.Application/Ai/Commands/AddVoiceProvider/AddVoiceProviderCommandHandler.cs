using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.AddVoiceProvider;

public sealed class AddVoiceProviderCommandHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IAiCredentialProtector credentialProtector,
    IAIProviderRepository aiProviders,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<AddVoiceProviderCommandHandler> logger) : IRequestHandler<AddVoiceProviderCommand, AdminVoiceProviderDto>
{
    public async Task<AdminVoiceProviderDto> Handle(AddVoiceProviderCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var engine = VoiceEngineResolution.FindEngine(engines, request.ProviderKey)
            ?? throw new KeyNotFoundException("No text-to-speech engine with that key is installed.");

        var existing = await voiceProviders.ListByPriorityAsync(cancellationToken);
        if (existing.Any(p => string.Equals(p.ProviderKey, engine.ProviderKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateResourceException($"{engine.DisplayName} has already been added.");
        }

        var nextPriority = existing.Count == 0 ? 0 : existing.Max(p => p.Priority) + 1;
        var provider = VoiceProvider.Create(engine.ProviderKey, engine.DisplayName, nextPriority, actorUserId);

        var vendor = await VoiceEngineResolution.FindVendorAsync(aiProviders, engine.ProviderKey, cancellationToken);
        if (vendor is not null && !string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new DomainRuleViolationException(
                $"Set the {vendor.DisplayName} API key under Admin → AI providers — it is shared with its health check, models and live dictation.");
        }

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            var apiKey = request.ApiKey.Trim();
            provider.SetCredential(credentialProtector.Protect(apiKey), CredentialHintFormatter.Format(apiKey), actorUserId);
        }

        voiceProviders.Add(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // CA1873 — the detail string is only built when Information logging is enabled.
        if (logger.IsEnabled(LogLevel.Information))
        {
            var detail = $"Added {engine.ProviderKey} at priority {nextPriority}";
            AiAdminActionLog.AdminVoiceProviderActionPerformed(logger, "AddVoiceProvider", actorUserId, provider.Id, detail);
        }

        return await VoiceEngineResolution.ToDtoAsync(provider, isPrimary: nextPriority == 0, engine, vendor, cancellationToken);
    }
}
