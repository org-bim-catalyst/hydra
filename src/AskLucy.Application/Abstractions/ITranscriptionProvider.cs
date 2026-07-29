namespace AskLucy.Application.Abstractions;

/// <summary>
/// Deliberately separate from <see cref="IAIProvider"/> (whose sole implementation is
/// pinned to OpenAI per FR-022) — this covers the mic-dictation path specifically, which
/// runs against a free, self-hosted model rather than a paid hosted AI vendor, so it isn't
/// subject to that single-provider constraint. The audio must be 16-bit PCM WAV; unlike
/// <see cref="IAIProvider.TranscribeAudioAsync"/>, there is no server-side format
/// conversion, so arbitrary uploaded audio files still go through the OpenAI path.
/// </summary>
public interface ITranscriptionProvider
{
    Task<string> TranscribeAsync(Stream wavAudio, CancellationToken cancellationToken = default);
}
