using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.Transcribe;

public sealed class TranscribeAudioCommandHandler(IAIProvider aiProvider) : IRequestHandler<TranscribeAudioCommand, string>
{
    public Task<string> Handle(TranscribeAudioCommand request, CancellationToken cancellationToken) =>
        aiProvider.TranscribeAudioAsync(request.Audio, request.FileName, request.ContentType, cancellationToken);
}
