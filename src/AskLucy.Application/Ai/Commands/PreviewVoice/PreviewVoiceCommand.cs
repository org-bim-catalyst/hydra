using MediatR;

namespace AskLucy.Application.Ai.Commands.PreviewVoice;

/// <summary>
/// specs/070 contracts/admin-voice.md `POST /api/v1/admin/voice/providers/{id}/preview` — the
/// sample sentence's Play button. Synthesizes through the named provider only, never failing over:
/// the administrator is auditioning this specific voice.
/// </summary>
public sealed record PreviewVoiceCommand(Guid ProviderId, string VoiceId, string Text, string Language) : IRequest<VoicePreviewDto>;
