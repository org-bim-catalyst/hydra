namespace AskLucy.Domain.Memory;

/// <summary>contracts/memories-api.md's approve/reject `409 Conflict` — only valid while a memory is <see cref="MemoryLifecycleState.PendingApproval"/>.</summary>
public sealed class MemoryNotPendingApprovalException() : Exception("Only a memory pending approval can be approved or rejected.");
