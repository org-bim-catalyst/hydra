using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;

/// <summary>contracts/voice-stt-session.md `POST /api/v1/ai/voice/stt-session`.</summary>
public sealed record CreateSpeechToTextSessionCommand(string Language) : IRequest<SpeechToTextSession>;
