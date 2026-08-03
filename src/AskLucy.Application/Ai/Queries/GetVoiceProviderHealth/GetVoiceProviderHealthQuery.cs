using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceProviderHealth;

/// <summary>contracts/voice-provider-health.md — admin-only aggregate view of voice
/// provider failover/recovery activity. <c>FromUtc</c>/<c>ToUtc</c> default to the last 24h
/// when omitted (handler-side, so callers never need to compute "now" themselves).</summary>
public sealed record GetVoiceProviderHealthQuery(DateTime? FromUtc, DateTime? ToUtc) : IRequest<VoiceProviderHealthDto>;
