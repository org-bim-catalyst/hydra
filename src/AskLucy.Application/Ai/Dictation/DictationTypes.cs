namespace AskLucy.Application.Ai.Dictation;

/// <summary>specs/078 — the text one dictation clip produced.</summary>
public sealed record DictationTranscript(string Text, string? DetectedLanguage, TimeSpan Elapsed);

/// <summary>
/// specs/078 — one recorded clip, already checked to be 16 kHz mono PCM WAV.
/// <paramref name="LocalWhisperModelPath"/> is the resolved Local Whisper model file, set only
/// when Local Whisper transcribes the clip.
/// </summary>
public sealed record DictationClip(Stream Wav, string? Language, string? LocalWhisperModelPath = null);

/// <summary>specs/078 — what <see cref="Abstractions.ILocalWhisperModelCatalog.ResolveSelectedAsync"/> found.</summary>
public abstract record LocalWhisperModelResolution
{
    private LocalWhisperModelResolution()
    {
    }

    /// <summary>No model is selected: a fresh deployment. The browser built-in serves; nothing failed.</summary>
    public sealed record None : LocalWhisperModelResolution
    {
        public static readonly None Instance = new();
    }

    /// <summary>The selected deployment is marked Unavailable (or gone). The browser built-in serves; nothing failed (FR-010).</summary>
    public sealed record Unavailable(string Reason, string? ModelLabel) : LocalWhisperModelResolution;

    /// <summary>The selected deployment's file is missing or unreadable: a Local Whisper failure (FR-007).</summary>
    public sealed record Broken(string Reason, string ModelLabel) : LocalWhisperModelResolution;

    public sealed record Ready(string ModelPath, string ModelLabel, string FileName) : LocalWhisperModelResolution;
}

/// <summary>specs/078 — one Custom Models deployment as a Local Whisper model choice.</summary>
public sealed record LocalWhisperModelOption(Guid Id, string Label, bool Selectable, string? Reason);

/// <summary>specs/078 — another "Try it" is still running.</summary>
public sealed class LocalWhisperTrialBusyException() : Exception("Another Local Whisper model try is still running. Try again when it finishes.");
