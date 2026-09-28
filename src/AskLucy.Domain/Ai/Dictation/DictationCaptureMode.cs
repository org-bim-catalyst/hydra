namespace AskLucy.Domain.Ai.Dictation;

/// <summary>specs/078 — how the user is dictating. Push-to-Talk always records a clip; Continuous streams when the engine can.</summary>
public enum DictationCaptureMode
{
    Continuous,
    PushToTalk,
}
