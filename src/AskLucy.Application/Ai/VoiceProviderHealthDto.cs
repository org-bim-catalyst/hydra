namespace AskLucy.Application.Ai;

/// <summary>contracts/voice-provider-health.md — one row of the admin failover/recovery log.
/// <c>Direction</c> is the stringified <c>VoiceProviderFailoverDirection</c> (no global
/// enum-to-string JSON converter exists in this API yet — see
/// <see cref="UserVoicePreferenceDto"/> for the same convention).</summary>
public sealed record VoiceProviderFailoverEventDto(DateTime OccurredAtUtc, string Direction, string? Reason);

/// <summary>contracts/voice-provider-health.md — <c>CurrentStatus</c> is derived at query time,
/// never stored (research.md Decision 5).</summary>
public sealed record VoiceProviderHealthDto(
    string CurrentStatus,
    int FailoverCount,
    int RecoveryCount,
    IReadOnlyList<VoiceProviderFailoverEventDto> Events);
