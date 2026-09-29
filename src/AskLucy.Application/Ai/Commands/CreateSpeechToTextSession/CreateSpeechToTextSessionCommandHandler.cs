using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Ai.Dictation;
using MediatR;

namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>
/// Answers "how should this turn be dictated?" (contracts/dictation-session.md, research D6).
/// Makes exactly one attempt — the client (<c>useSpeechRecognition.ts</c>/<c>useVoiceRecorder.ts</c>)
/// owns the bounded reconnect/retry policy (research.md Decision 8) and calls this command again
/// on failure, rather than this handler retrying internally.
///
/// specs/074 US2 — an ElevenLabs mint failure is also a failover on the operational failure trail
/// (the browser's own recogniser serves the user), and the next session this provider mints for
/// the same user is the recovery that pairs with it. A provider an administrator has switched off
/// is not a failure: the browser built-in serves, and nothing is recorded.
///
/// specs/078 US3 — a Critical mint failure also suspends ElevenLabs realtime (FR-016), and a
/// Suspended engine's future turns short-circuit straight to the browser built-in with no vendor
/// call at all.
/// </summary>
public sealed class CreateSpeechToTextSessionCommandHandler(
    ISpeechToTextSessionProvider sessionProvider,
    IVoiceProviderHealthRecorder healthRecorder,
    IVoiceProviderFailoverEventRepository failoverEvents,
    IVoiceFailureReporter failureReporter,
    ICurrentUserAccessor currentUser,
    IDictationEngineSettingRepository settings,
    ILocalWhisperModelCatalog catalog,
    DictationVendorGate vendorGate,
    DictationFailurePolicy failurePolicy,
    IUnitOfWork unitOfWork) : IRequestHandler<CreateSpeechToTextSessionCommand, DictationSession>
{
    public async Task<DictationSession> Handle(CreateSpeechToTextSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var setting = await settings.GetOrCreateAsync(cancellationToken);
        var turnEngine = setting.ResolveEngine(request.Mode);

        if (setting.IsSuspendedFor(turnEngine))
        {
            return DictationSession.Browser(degraded: true);
        }

        return turnEngine switch
        {
            DictationTurnEngine.LocalWhisper => await ResolveLocalWhisperAsync(setting.LocalWhisperModelId, cancellationToken),

            // The Push-to-Talk engine is deliberately Browser (FR-017): a configuration choice,
            // not a failure.
            DictationTurnEngine.Browser => DictationSession.Browser(degraded: false),

            DictationTurnEngine.ElevenLabsRealtime => await MintRealtimeSessionAsync(setting, request.Language, userId, cancellationToken),

            _ => await ResolveOpenAiWhisperAsync(cancellationToken),
        };
    }

    /// <summary>
    /// specs/078 research D6 — OpenAI Whisper's local health check: switched on and its DB
    /// credential (when stored) decrypts. No network call is made, so a problem here is reported as
    /// a failover but never suspends the engine (US3/T066 suspends only a real transcription
    /// failure, which the caller's own next clip attempt would surface).
    /// </summary>
    private async Task<DictationSession> ResolveOpenAiWhisperAsync(CancellationToken cancellationToken)
    {
        var problem = await vendorGate.OpenAiWhisperHealthProblemAsync(cancellationToken);
        if (problem is null)
        {
            return DictationSession.Clip;
        }

        var engine = new VoiceEngineIdentity("OpenAI Whisper");
        failureReporter.ReportFailover(VoiceOperations.Transcription, engine, new InvalidOperationException(problem), fallbackServed: true, cancellationToken);
        return DictationSession.Browser(degraded: true);
    }

    private async Task<DictationSession> ResolveLocalWhisperAsync(Guid? modelId, CancellationToken cancellationToken)
    {
        var resolution = await catalog.ResolveSelectedAsync(modelId, cancellationToken);
        return resolution switch
        {
            LocalWhisperModelResolution.Ready => DictationSession.Clip,

            // FR-002/FR-010: neither a fresh deployment nor an administrator marking a deployment
            // Unavailable is a failure, so nothing is reported.
            LocalWhisperModelResolution.None or LocalWhisperModelResolution.Unavailable =>
                DictationSession.Browser(degraded: false),

            // A Broken model (its file missing/unreadable) is a Local Whisper failure (FR-007):
            // the browser built-in serves instead, degraded.
            LocalWhisperModelResolution.Broken broken => ReportBrokenModel(broken, cancellationToken),

            _ => DictationSession.Browser(degraded: false),
        };
    }

    private DictationSession ReportBrokenModel(LocalWhisperModelResolution.Broken broken, CancellationToken cancellationToken)
    {
        var engine = new VoiceEngineIdentity("Local Whisper", null, broken.ModelLabel);
        failurePolicy.ReportEngineFailure(
            VoiceOperations.Transcription, engine, isCloudEngine: false, new LocalWhisperModelBrokenException(broken.Reason), cancellationToken);
        return DictationSession.Browser(degraded: true);
    }

    private async Task<DictationSession> MintRealtimeSessionAsync(
        DictationEngineSetting setting, string language, string userId, CancellationToken cancellationToken)
    {
        if (!await sessionProvider.IsSwitchedOnAsync(cancellationToken))
        {
            return DictationSession.Browser(degraded: false);
        }

        var engine = new VoiceEngineIdentity(sessionProvider.ProviderName);

        try
        {
            var session = await sessionProvider.CreateSessionAsync(language, cancellationToken);
            failureReporter.ReportServed(VoiceOperations.Transcription, engine);

            // FR-034/SC-010: only record a recovery when the user's most recent event shows
            // they were actually degraded — a normal, uneventful success is not itself logged
            // (contracts/dictation-session.md).
            var mostRecent = await failoverEvents.GetMostRecentForUserAsync(userId, cancellationToken);
            if (mostRecent?.Direction == VoiceProviderFailoverDirection.FailedOverToFallback)
            {
                await healthRecorder.RecordRecoveryAsync(userId, cancellationToken);
            }

            return DictationSession.Realtime(session.Token, session.ExpiresAtUtc);
        }
        catch (AiProviderException ex)
        {
            var suspended = failurePolicy.ReportAndMaybeSuspend(
                VoiceOperations.Transcription, engine, setting, DictationPrimaryEngine.ElevenLabsRealtime, ex, cancellationToken);
            if (suspended)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await healthRecorder.RecordFailoverAsync(userId, FailureReasonSanitizer.Sanitize(ex.Message), cancellationToken);
            throw;
        }
    }
}
