using MediatR;

namespace AskLucy.Application.Ai.Queries.GetVoiceEngines;

/// <summary>specs/070 contracts/admin-voice.md `GET /api/v1/admin/voice/engines` — every installed text-to-speech engine, flagged with whether it has been added as a provider yet (what the "+" dialog offers).</summary>
public sealed record GetVoiceEnginesQuery : IRequest<IReadOnlyList<VoiceEngineDto>>;
