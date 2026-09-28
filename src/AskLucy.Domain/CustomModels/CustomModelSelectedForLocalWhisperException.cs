namespace AskLucy.Domain.CustomModels;

/// <summary>
/// specs/078 FR-009b: the deployment is the model Local Whisper is set to use, so it can't be
/// removed until the administrator selects a different one. Mapped to 409, like
/// <see cref="CustomModelNotInProgressException"/>: the current selection refuses, not the request.
/// </summary>
public sealed class CustomModelSelectedForLocalWhisperException()
    : Exception("Select a different Local Whisper model first.");
