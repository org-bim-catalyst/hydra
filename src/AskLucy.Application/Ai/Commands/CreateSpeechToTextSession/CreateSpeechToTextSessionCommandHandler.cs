using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>
/// Mints a primary-provider STT session token (contracts/voice-stt-session.md). Makes exactly
/// one attempt — the client (<c>useSpeechRecognition.ts</c>) owns the bounded reconnect/retry
/// policy (research.md Decision 8) and calls this command again on failure, rather than this
/// handler retrying internally.
/// </summary>
public sealed class CreateSpeechToTextSessionCommandHandler(
    ISpeechToTextSessionProvider sessionProvider,
    IVoiceProviderHealthRecorder healthRecorder,
    IVoiceProviderFailoverEventRepository failoverEvents,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateSpeechToTextSessionCommand, SpeechToTextSession>
{
    public async Task<SpeechToTextSession> Handle(CreateSpeechToTextSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        try
        {
            var session = await sessionProvider.CreateSessionAsync(request.Language, cancellationToken);

            // FR-034/SC-010: only record a recovery when the user's most recent event shows
            // they were actually degraded — a normal, uneventful success is not itself logged
            // (contracts/voice-stt-session.md).
            var mostRecent = await failoverEvents.GetMostRecentForUserAsync(userId, cancellationToken);
            if (mostRecent?.Direction == VoiceProviderFailoverDirection.FailedOverToFallback)
            {
                await healthRecorder.RecordRecoveryAsync(userId, cancellationToken);
            }

            return session;
        }
        catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderRateLimitedException or AiProviderAuthenticationException)
        {
            var reason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            await healthRecorder.RecordFailoverAsync(userId, reason, cancellationToken);
            throw;
        }
    }
}
