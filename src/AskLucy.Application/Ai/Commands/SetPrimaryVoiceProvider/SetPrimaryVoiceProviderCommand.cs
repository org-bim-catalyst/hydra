using MediatR;

namespace AskLucy.Application.Ai.Commands.SetPrimaryVoiceProvider;

/// <summary>
/// specs/070 contracts/admin-voice.md `PUT /api/v1/admin/voice/primary` — "Set as Lucy's voice".
/// Makes the provider priority 0 speaking with <see cref="VoiceId"/>; every other provider keeps
/// its relative order behind it as a failover.
/// </summary>
public sealed record SetPrimaryVoiceProviderCommand(Guid ProviderId, string VoiceId) : IRequest<IReadOnlyList<AdminVoiceProviderDto>>;
