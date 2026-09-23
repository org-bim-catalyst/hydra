using MediatR;

namespace AskLucy.Application.Ai.Commands.SetVoiceProviderCredential;

/// <summary>specs/070 contracts/admin-voice.md `PUT /api/v1/admin/voice/providers/{id}/credential`. The plaintext key never survives past this command's handler.</summary>
public sealed record SetVoiceProviderCredentialCommand(Guid ProviderId, string ApiKey) : IRequest<AdminVoiceProviderDto>;
