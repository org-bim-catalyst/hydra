namespace AskLucy.Domain.Ai.Dictation;

/// <summary>specs/078 — the engine that serves one dictation turn, as resolved by <see cref="DictationEngineSetting.ResolveEngine"/>.</summary>
public enum DictationTurnEngine
{
    LocalWhisper,
    OpenAiWhisper,
    ElevenLabsRealtime,
    Browser,
}
