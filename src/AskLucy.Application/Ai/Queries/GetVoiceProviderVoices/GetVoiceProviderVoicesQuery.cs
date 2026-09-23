using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceProviderVoices;

/// <summary>specs/070 contracts/admin-voice.md `GET /api/v1/admin/voice/providers/{id}/voices` — the voices the provider's engine can speak with.</summary>
public sealed record GetVoiceProviderVoicesQuery(Guid ProviderId) : IRequest<IReadOnlyList<VoiceOptionDto>>;
