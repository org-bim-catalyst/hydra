using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.TranscribeMicrophoneAudio;

public sealed class TranscribeMicrophoneAudioCommandHandler(ITranscriptionProvider transcriptionProvider)
    : IRequestHandler<TranscribeMicrophoneAudioCommand, string>
{
    public Task<string> Handle(TranscribeMicrophoneAudioCommand request, CancellationToken cancellationToken) =>
        transcriptionProvider.TranscribeAsync(request.WavAudio, cancellationToken);
}
