using AskLucy.Domain.Memory;
using MediatR;

namespace AskLucy.Application.Memory.Commands.CreateMemoryCandidate;

/// <summary>
/// Creates a <see cref="Domain.Memory.Memory"/> candidate on behalf of an explicitly-supplied
/// user (spec.md FR-031, research.md Decision 5) — the one integration seam the Agent Framework
/// needs from the Memory Engine (specs/020-ai-agent-framework), reusing its existing
/// <c>PendingApproval</c> lifecycle rather than building a second one. Takes <see cref="UserId"/>
/// explicitly rather than resolving it from <c>ICurrentUserAccessor</c> so this command is safe
/// to call from a background job with no HTTP context (the Agent Runtime), not only from a
/// live authenticated request. Returns <c>null</c> when the category is Disabled for this user —
/// nothing is created, mirroring <c>MemoryExtractionJob</c>'s identical short-circuit.
/// </summary>
public sealed record CreateMemoryCandidateCommand(
    string UserId,
    Guid? ProjectId,
    MemoryCategory Category,
    string Content,
    decimal Importance,
    decimal Confidence,
    bool IsSensitive) : IRequest<Guid?>;
