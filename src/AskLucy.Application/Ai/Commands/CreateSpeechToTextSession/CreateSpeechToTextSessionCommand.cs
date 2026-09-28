using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai.Dictation;
using MediatR;

namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>
/// contracts/dictation-session.md `POST /api/v1/ai/voice/stt-session`. <paramref name="Mode"/>
/// defaults to Continuous so a tab loaded before this deploy keeps working.
/// </summary>
public sealed record CreateSpeechToTextSessionCommand(string Language, DictationCaptureMode Mode = DictationCaptureMode.Continuous)
    : IRequest<DictationSession>;
