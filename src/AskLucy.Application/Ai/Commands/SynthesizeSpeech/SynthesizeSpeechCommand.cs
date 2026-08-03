using MediatR;

namespace AskLucy.Application.Ai.Commands.SynthesizeSpeech;

/// <summary>
/// `POST /api/v1/ai/voice/speak` — synthesizes speech for a piece of text that already
/// exists (FR-006's "speak every AI reply aloud" behavior), as opposed to
/// <see cref="Commands.StreamVoiceReply.StreamVoiceReplyCommand"/>, which generates a new LLM
/// reply and speaks it as the text streams in. No chat/provider/model context is needed here
/// — the caller already has the text.
/// </summary>
public sealed record SynthesizeSpeechCommand(string Text, string Language) : IStreamRequest<VoiceReplyEvent>;
