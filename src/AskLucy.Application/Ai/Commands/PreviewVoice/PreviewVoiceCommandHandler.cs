using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.PreviewVoice;

public sealed class PreviewVoiceCommandHandler(
    IVoiceProviderRepository voiceProviders,
    IEnumerable<ITextToSpeechEngine> engines,
    IAiCredentialProtector credentialProtector,
    ICurrentUserAccessor currentUser,
    ILogger<PreviewVoiceCommandHandler> logger) : IRequestHandler<PreviewVoiceCommand, VoicePreviewDto>
{
    /// <summary>Every <see cref="ITextToSpeechEngine"/> yields MP3.</summary>
    public const string AudioContentType = "audio/mpeg";

    public async Task<VoicePreviewDto> Handle(PreviewVoiceCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var provider = await voiceProviders.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Voice provider not found.");

        var engine = VoiceEngineResolution.GetEngine(engines, provider);
        var apiKey = VoiceEngineResolution.DecryptCredential(credentialProtector, provider);
        var settings = engine.ResolveDefaultSettings(request.Language, request.VoiceId) with
        {
            VoiceId = request.VoiceId,
            Language = request.Language,
            ProviderKey = engine.ProviderKey,
        };

        using var audio = new MemoryStream();
        await foreach (var chunk in engine.StreamSpeechAsync(request.Text.Trim(), settings, apiKey, cancellationToken))
        {
            await audio.WriteAsync(chunk, cancellationToken);
        }

        if (audio.Length == 0)
        {
            throw new AiProviderUnavailableException($"{provider.DisplayName} returned no audio for this sentence.");
        }

        AiAdminActionLog.AdminVoiceProviderActionPerformed(
            logger, "PreviewVoice", actorUserId, provider.Id, $"Previewed voice {request.VoiceId} ({request.Language}, {audio.Length} bytes)");

        return new VoicePreviewDto(Convert.ToBase64String(audio.GetBuffer(), 0, (int)audio.Length), AudioContentType);
    }
}
