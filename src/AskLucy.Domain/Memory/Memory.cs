using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>Closed set of memory categories (spec.md FR-001). <c>KnowledgeDerived</c> is deferred to a future iteration per spec.md Assumptions — not modeled yet.</summary>
public enum MemoryCategory
{
    UserPreference,
    PersonalFact,
    ProjectContext,
    ConversationDerived,
}

/// <summary>
/// A memory's current lifecycle position (spec.md FR-005). Collapses spec.md's five-state list
/// ("detected/candidate, pending review, approved/active, updated, archived, deleted") into three
/// persisted values — discovered while implementing the actual state-transition logic
/// (data-model.md originally listed <c>Candidate</c> and <c>PendingApproval</c> as distinct
/// values, but nothing ever observably occupies <c>Candidate</c> on its own: a memory's
/// destination state is decided synchronously at creation time by <see cref="Memory.CreateCandidate"/>).
/// "Updated" is an *event*, not a state — an edit keeps <see cref="MemoryLifecycleState.Active"/>
/// and appends a <see cref="MemoryVersion"/> row instead. "Deleted" is the standard
/// <see cref="BaseEntity.IsDeleted"/> soft-delete flag, not a fourth enum value, matching every
/// other retention-sensitive entity in this codebase (constitution §5).
/// </summary>
public enum MemoryLifecycleState
{
    PendingApproval,
    Active,
    Archived,
}

/// <summary>Where a memory candidate originated (spec.md FR-006, Key Entity "Memory Source"). <c>ProjectConfiguration</c>/<c>Integration</c> are reserved fields per spec.md's "Memory Creation" section — no handler emits them yet. <c>AgentProposed</c> added for specs/020-ai-agent-framework's <c>MemoryWriteTool</c> (research.md Decision 5) — an agent-proposed candidate is distinguishable from passive extraction/explicit statements in the approval queue.</summary>
public enum MemorySourceType
{
    ExplicitUserStatement,
    PassiveConversationAnalysis,
    ProjectConfiguration,
    Integration,
    AgentProposed,
}

/// <summary>
/// The core aggregate — a single remembered fact or preference (spec.md FR-001–FR-014a,
/// data-model.md). <see cref="Content"/> is encrypted at rest via an <c>IAiCredentialProtector</c>
/// -backed value converter (research.md Decision 12, reusing the existing protector interface
/// rather than inventing a parallel one — discovered during <c>/speckit-implement</c> that its
/// <c>Protect(string)</c>/<c>Unprotect(string)</c> shape is already fully generic, not
/// credential-specific despite its name).
/// </summary>
public sealed class Memory : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public Guid? ProjectId { get; private set; }

    public MemoryCategory Category { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public MemoryLifecycleState State { get; private set; }

    /// <summary>Set by extraction classification (spec.md FR-008). Forces manual approval for this row regardless of the user's category-level <see cref="MemoryApprovalMode"/> — enforced in <see cref="CreateCandidate"/>.</summary>
    public bool IsSensitive { get; private set; }

    public MemorySourceType SourceType { get; private set; }

    public Guid? SourceConversationId { get; private set; }

    /// <summary>0.00–1.00 (spec.md FR-010). Set by extraction classification; user-editable via the Memory Center.</summary>
    public decimal Importance { get; private set; }

    /// <summary>0.00–1.00 (spec.md FR-010). Reflects the system's own certainty — not user-editable.</summary>
    public decimal Confidence { get; private set; }

    public DateTime LastReinforcedAtUtc { get; private set; }

    public int FrequencyCount { get; private set; }

    /// <summary>Only set for explicitly time-bound memories (spec.md Assumptions — no default expiration otherwise).</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    private Memory()
    {
        // Required by EF Core materialization.
    }

    /// <summary>
    /// Creates a memory candidate, deciding its initial <see cref="MemoryLifecycleState"/> from
    /// the category's configured <paramref name="approvalMode"/> and <paramref name="isSensitive"/>
    /// (spec.md FR-007, FR-008) — a sensitive candidate always starts <see cref="MemoryLifecycleState.PendingApproval"/>
    /// regardless of the configured mode. Callers must never invoke this for a category whose mode
    /// is <see cref="MemoryApprovalMode.Disabled"/> — that decision (create nothing at all) is made
    /// by the caller before reaching this factory, not encoded as a fourth lifecycle state.
    /// </summary>
    public static Memory CreateCandidate(
        string userId, Guid? projectId, MemoryCategory category, string content,
        MemorySourceType sourceType, Guid? sourceConversationId,
        decimal importance, decimal confidence, bool isSensitive,
        MemoryApprovalMode approvalMode, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A memory must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainRuleViolationException("A memory must have content.");
        }

        if (approvalMode == MemoryApprovalMode.Disabled)
        {
            throw new DomainRuleViolationException("Cannot create a memory candidate for a disabled category.");
        }

        var initialState = isSensitive || approvalMode == MemoryApprovalMode.Manual
            ? MemoryLifecycleState.PendingApproval
            : MemoryLifecycleState.Active;

        var now = DateTime.UtcNow;

        return new Memory
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ProjectId = projectId,
            Category = category,
            Content = content.Trim(),
            State = initialState,
            IsSensitive = isSensitive,
            SourceType = sourceType,
            SourceConversationId = sourceConversationId,
            Importance = Clamp(importance),
            Confidence = Clamp(confidence),
            LastReinforcedAtUtc = now,
            FrequencyCount = 1,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }

    /// <summary>spec.md FR-021, User Story 3 AC2.</summary>
    public void Approve(string actor)
    {
        if (State != MemoryLifecycleState.PendingApproval)
        {
            throw new DomainRuleViolationException("Only a pending memory can be approved.");
        }

        State = MemoryLifecycleState.Active;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>spec.md FR-021, User Story 3 AC3 — a rejected candidate is discarded (soft-deleted), never used.</summary>
    public void Reject(string actor)
    {
        if (State != MemoryLifecycleState.PendingApproval)
        {
            throw new DomainRuleViolationException("Only a pending memory can be rejected.");
        }

        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>spec.md FR-009, FR-019/FR-015. Returns the content as it was *before* this edit, so the caller can construct the corresponding <see cref="MemoryVersion"/> row.</summary>
    public string Edit(string newContent, string actor)
    {
        if (string.IsNullOrWhiteSpace(newContent))
        {
            throw new DomainRuleViolationException("A memory must have content.");
        }

        var previousContent = Content;
        Content = newContent.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
        return previousContent;
    }

    /// <summary>spec.md User Story 5 AC3 (Project deletion cascade) and background cleanup (FR-031). Idempotent, mirrors <c>UserChat.Archive</c>.</summary>
    public void Archive(string actor)
    {
        if (State == MemoryLifecycleState.Archived)
        {
            return;
        }

        State = MemoryLifecycleState.Archived;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>spec.md FR-010, Edge Case "same fact stated many times" — reinforces recency/frequency instead of creating a duplicate.</summary>
    public void Reinforce(string actor)
    {
        LastReinforcedAtUtc = DateTime.UtcNow;
        FrequencyCount++;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Reclassifies an already-created memory as sensitive (spec.md FR-008) — idempotent.</summary>
    public void MarkSensitive(string actor)
    {
        if (IsSensitive)
        {
            return;
        }

        IsSensitive = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>spec.md FR-020 — immediately excluded from all future retrieval via the standard soft-delete query filter.</summary>
    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }

    public bool IsOwnedBy(string userId) => UserId == userId;

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1m);
}
