namespace AskLucy.Application.Ai.Deprecation;

/// <summary>specs/069 FR-021/FR-022: Deprecated is terminal and only the sync review may enter it. Mapped to 409.</summary>
public sealed class ModelStatusLockedException(string message) : Exception(message);
