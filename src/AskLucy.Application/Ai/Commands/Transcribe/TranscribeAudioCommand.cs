using MediatR;

namespace AskLucy.Application.Ai.Commands.Transcribe;

public sealed record TranscribeAudioCommand(Stream Audio, string FileName, string ContentType) : IRequest<string>;
