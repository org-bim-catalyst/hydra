using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>
/// Bound from configuration (constitution §4). Per-turn bounds for the conversational agent
/// runtime (specs/045-conversational-agent-runtime, FR-002/FR-020/FR-023).
/// <para>
/// Deliberately separate from <see cref="AgentRuntimeOptions"/> rather than folded into it: that
/// type bounds a background agent execution, which may legitimately run for fifteen minutes and
/// fifty tool calls. A chat turn happens while a person waits, so its ceilings are two orders of
/// magnitude smaller and would be nonsense as defaults for the other. Two names, two reasons to
/// change (constitution §2.II).
/// </para>
/// </summary>
public sealed class ConversationRuntimeOptions
{
    public const string SectionName = "ConversationRuntime";

    /// <summary>FR-002 — hard cap on capability invocations in one turn; the loop can never recurse past this.</summary>
    [Range(1, 20)]
    public int MaxCapabilityInvocationsPerTurn { get; init; } = 3;

    /// <summary>FR-016/FR-019 — how many sub-agent slices one turn may fan out to.</summary>
    [Range(1, 10)]
    public int MaxDelegationsPerTurn { get; init; } = 3;

    /// <summary>
    /// FR-009/FR-059 — aggregate wall-clock ceiling for the whole turn. Set above the slowest
    /// single capability (site-boundary resolution budgets ~45 s) so a legitimate long step is
    /// not cut off by the turn budget it sits inside.
    /// </summary>
    [Range(5, 600)]
    public int MaxTurnDurationSeconds { get; init; } = 90;

    /// <summary>FR-023 — total rows in an offer, including the decline. At most four substantive choices.</summary>
    [Range(2, 10)]
    public int MaxSuggestedActions { get; init; } = 5;

    /// <summary>
    /// research.md D14 — at or below this many available capabilities the whole Tier 1 index is
    /// shown; above it, embedding retrieval narrows what the deciding model sees.
    /// </summary>
    [Range(1, 200)]
    public int IndexRetrievalThreshold { get; init; } = 10;

    /// <summary>research.md D14 — how many entries retrieval keeps, before context-gated capabilities are added back.</summary>
    [Range(1, 100)]
    public int IndexRetrievalTopN { get; init; } = 8;

    /// <summary>
    /// FR-005a — an action expected to take longer than this is announced as its own message
    /// before it starts. Flow steps are announced regardless (FR-052); this governs standalone
    /// capability invocations only.
    /// </summary>
    [Range(0, 60)]
    public int AnnounceThresholdSeconds { get; init; } = 2;

    /// <summary>research.md D5 — SSE keep-alive interval while a beat is pending, so a proxy cannot silently buffer the stream.</summary>
    [Range(1, 60)]
    public int KeepAliveIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// specs/045 T042 — the per-invocation budget for a <see cref="Capabilities.CapabilityDuration.Brief"/>
    /// capability, applied as a linked-token timeout around <c>IConversationCapability.ExecuteAsync</c>
    /// (the specs/044 <c>ResolveBoundarySafelyAsync</c> pattern, generalised to every capability
    /// rather than one boundary-specific call site).
    /// </summary>
    [Range(1, 60)]
    public int BriefCapabilityTimeoutSeconds { get; init; } = 10;

    /// <summary>As above, for <see cref="Capabilities.CapabilityDuration.Noticeable"/>.</summary>
    [Range(1, 120)]
    public int NoticeableCapabilityTimeoutSeconds { get; init; } = 30;

    /// <summary>As above, for <see cref="Capabilities.CapabilityDuration.Extended"/> — set above site-boundary resolution's own ~45s worst case.</summary>
    [Range(1, 300)]
    public int ExtendedCapabilityTimeoutSeconds { get; init; } = 90;
}
