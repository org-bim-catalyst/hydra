namespace AskLucy.Application.Abstractions;

/// <summary>Records voice-session failover/recovery events (FR-033/FR-034/FR-039) — the
/// Application-layer entry point <c>CreateSpeechToTextSessionCommandHandler</c> and
/// <c>StreamVoiceReplyCommandHandler</c> call on primary-provider failure/recovery, backed by
/// <c>VoiceProviderFailoverEvent</c> (data-model.md).</summary>
public interface IVoiceProviderHealthRecorder
{
    Task RecordFailoverAsync(string userId, string reason, CancellationToken cancellationToken = default);

    Task RecordRecoveryAsync(string userId, CancellationToken cancellationToken = default);
}
