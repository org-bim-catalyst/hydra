using MediatR;

namespace AskLucy.Application.Ai.Commands.AddVoiceProvider;

/// <summary>
/// specs/070 contracts/admin-voice.md `POST /api/v1/admin/voice/providers` — the "+" button.
/// Adds an installed engine as the lowest-priority voice provider (the last failover); the
/// plaintext key, if any, never survives past this command's handler.
/// </summary>
public sealed record AddVoiceProviderCommand(string ProviderKey, string? ApiKey) : IRequest<AdminVoiceProviderDto>;
