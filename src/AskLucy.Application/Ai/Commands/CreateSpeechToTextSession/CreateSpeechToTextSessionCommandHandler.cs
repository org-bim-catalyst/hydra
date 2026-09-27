using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>
/// Mints a primary-provider STT session token (contracts/voice-stt-session.md). Makes exactly
/// one attempt — the client (<c>useSpeechRecognition.ts</c>) owns the bounded reconnect/retry
/// policy (research.md Decision 8) and calls this command again on failure, rather than this
/// handler retrying internally.
///
/// specs/074 US2 — a failure is also a failover on the operational failure trail (the browser's
/// own recogniser serves the user), and the next session this provider mints for the same user is
/// the recovery that pairs with it.
///
/// A provider an administrator has switched off is not a failure: the session says to dictate
/// with Whisper, and nothing is recorded (the 2026-09-26 "Not configured" Critical incident was
/// ElevenLabs being switched off on purpose).
/// </summary>
public sealed class CreateSpeechToTextSessionCommandHandler(
    ISpeechToTextSessionProvider sessionProvider,
    IVoiceProviderHealthRecorder healthRecorder,
    IVoiceProviderFailoverEventRepository failoverEvents,
    IVoiceFailureReporter failureReporter,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateSpeechToTextSessionCommand, DictationSession>
{
    public async Task<DictationSession> Handle(CreateSpeechToTextSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        if (!await sessionProvider.IsSwitchedOnAsync(cancellationToken))
        {
            return DictationSession.Whisper;
        }

        var engine = new VoiceEngineIdentity(sessionProvider.ProviderName);

        try
        {
            var session = await sessionProvider.CreateSessionAsync(request.Language, cancellationToken);
            failureReporter.ReportServed(VoiceOperations.Transcription, engine);

            // FR-034/SC-010: only record a recovery when the user's most recent event shows
            // they were actually degraded — a normal, uneventful success is not itself logged
            // (contracts/voice-stt-session.md).
            var mostRecent = await failoverEvents.GetMostRecentForUserAsync(userId, cancellationToken);
            if (mostRecent?.Direction == VoiceProviderFailoverDirection.FailedOverToFallback)
            {
                await healthRecorder.RecordRecoveryAsync(userId, cancellationToken);
            }

            return DictationSession.Realtime(session.Token, session.ExpiresAtUtc);
        }
        // specs/068 - every provider failure, not the three that were listed. The named set left
        // out the two that occur most often in practice: NotConfigured (the provider is switched
        // off or has no key) and CredentialUnreadable. Those escaped uncaught, so the user was
        // silently pushed onto the fallback recogniser with nothing recorded, and the recovery
        // check above - which only fires when the most recent event says the user was degraded -
        // then never saw that they had been. The health trail showed an uninterrupted primary
        // provider for a user who had not reached it in weeks (constitution §2.VIII).
        catch (AiProviderException ex)
        {
            failureReporter.ReportFailover(VoiceOperations.Transcription, engine, ex, fallbackServed: true, cancellationToken);
            await healthRecorder.RecordFailoverAsync(userId, FailureReasonSanitizer.Sanitize(ex.Message), cancellationToken);
            throw;
        }
    }
}
