namespace AskLucy.Domain.Ai.Dictation;

/// <summary>
/// specs/078 FR-017 — the engine that transcribes a Push-to-Talk clip while ElevenLabs realtime
/// is the primary engine. ElevenLabs realtime streams, so it can't take a recorded clip.
/// </summary>
public enum DictationClipEngine
{
    LocalWhisper,
    OpenAiWhisper,
    Browser,
}
