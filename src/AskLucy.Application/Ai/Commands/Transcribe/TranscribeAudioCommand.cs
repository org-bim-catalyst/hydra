using MediatR;

namespace AskLucy.Application.Ai.Commands.Transcribe;

/// <param name="Language">The speaker's language when known (dictation); null for an uploaded file.</param>
public sealed record TranscribeAudioCommand(Stream Audio, string FileName, string ContentType, string? Language = null) : IRequest<string>;
