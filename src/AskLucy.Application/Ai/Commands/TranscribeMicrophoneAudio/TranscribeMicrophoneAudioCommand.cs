using MediatR;

namespace AskLucy.Application.Ai.Commands.TranscribeMicrophoneAudio;

/// <summary>Mic-dictation input (WAV only) — see <see cref="Abstractions.ITranscriptionProvider"/>.</summary>
public sealed record TranscribeMicrophoneAudioCommand(Stream WavAudio) : IRequest<string>;
