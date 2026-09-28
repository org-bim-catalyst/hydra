using AskLucy.Application.Ai.Dictation;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// specs/078 US4 "Try it" — transcribes one clip on a model that may not be selected. The model is
/// loaded for this call only and released afterward, so trying never changes what serves users.
/// One try runs at a time.
/// </summary>
public interface ILocalWhisperModelTrial
{
    /// <summary>Throws <see cref="LocalWhisperTrialBusyException"/> while another try is running.</summary>
    Task<DictationTranscript> TryAsync(string modelPath, Stream wav, string? language, CancellationToken cancellationToken = default);
}
