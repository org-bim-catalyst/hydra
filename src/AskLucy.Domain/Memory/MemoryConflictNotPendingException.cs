namespace AskLucy.Domain.Memory;

/// <summary>contracts/memories-api.md's resolve-conflict `409 Conflict` — only valid while the memory has an open (<see cref="MemoryConflictResolutionStatus.PendingUserConfirmation"/>) conflict.</summary>
public sealed class MemoryConflictNotPendingException() : Exception("This memory has no conflict awaiting resolution.");
