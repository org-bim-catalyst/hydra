namespace AskLucy.Application.Conversations.Runtime;

/// <summary>What a turn needs (specs/045 FR-001, contracts/turn-stream.md §1).</summary>
public enum TurnIntent
{
    /// <summary>Answerable in words. The fast path: no beats, no offer, no execution record (FR-006).</summary>
    Answer,

    /// <summary>The user asked for something to be done. Slices run.</summary>
    Act,

    /// <summary>
    /// The user asked <i>about</i> something rather than asking for it. Answer in words, then
    /// offer the related actions (FR-051a.2). Borderline cases resolve here, because offering
    /// wrongly costs a click while acting wrongly moves someone's map uninvited.
    /// </summary>
    Suggest,
}

/// <summary>
/// One unit of work the decision assigns (contracts/turn-stream.md §1).
/// </summary>
/// <param name="CapabilityKey">Must be in the turn's available set; anything else is dropped and logged (FR-014).</param>
/// <param name="ArgumentsJson">Raw arguments, validated against the capability's Tier 3 schema before it runs.</param>
/// <param name="PendingLabel">What the user reads while this runs. Falls back to the capability's own label when absent.</param>
/// <param name="DependsOn">0-based index of an earlier slice this one needs, or null.</param>
public sealed record TurnSlice(
    string CapabilityKey,
    string ArgumentsJson,
    string? PendingLabel,
    int? DependsOn);

/// <summary>
/// The decide step's verdict.
///
/// <para>
/// Note what is <b>absent</b>: an acknowledgement string. The first thing the user sees is
/// templated from the selected capability (research.md D15), so it is not waiting on this call
/// and is not lost when this call fails.
/// </para>
///
/// <para>
/// specs/045 Phase 6 (FR-050-FR-051) — <see cref="FlowKey"/> is set <b>instead of</b>
/// <see cref="Slices"/> when the decide step selects a whole job rather than a single capability;
/// a flow describes one outcome from one set of inputs, so it carries its own
/// <see cref="FlowArgumentsJson"/> rather than per-step arguments (research.md D17 — simple
/// binding, not an expression language). It is meaningful for two different intents: with
/// <see cref="TurnIntent.Act"/> it is what <see cref="IsFlowRun"/> runs; with
/// <see cref="TurnIntent.Suggest"/> it names which flow's variants the offer step should consider
/// (FR-051a.2) without running anything.
/// </para>
/// </summary>
public sealed record TurnDecision(
    TurnIntent Intent,
    IReadOnlyList<TurnSlice> Slices,
    string? FlowKey = null,
    string? FlowArgumentsJson = null,

    /// <summary>
    /// 0-based index of the last step an <see cref="TurnIntent.Act"/> flow run should execute
    /// (FR-058's scoping — "just find it" runs step 0 alone). Null runs every step. Meaningless
    /// outside a flow run; unlike a dispatched <c>FlowVariant</c> selection, which names a
    /// registered <see cref="Flows.FlowVariant"/> by key, a decide-step-driven run picks a plain
    /// step count because "just find it" has no need to be a nameable, offerable outcome.
    /// </summary>
    int? ThroughStepIndex = null)
{
    /// <summary>
    /// The safe verdict. Used for the fast path, and for every degraded path — an unparseable
    /// response, an unrecognised intent, a provider outage. Answering in words is always
    /// something the platform can do, so failure never leaves the user with nothing (FR-039).
    /// </summary>
    public static readonly TurnDecision AnswerOnly = new(TurnIntent.Answer, []);

    /// <summary>True when this decision names a flow to run now, rather than merely naming one worth offering.</summary>
    public bool IsFlowRun => Intent == TurnIntent.Act && FlowKey is not null;

    /// <summary>True when the turn should skip orchestration entirely (FR-006).</summary>
    public bool IsFastPath => Intent != TurnIntent.Act || (Slices.Count == 0 && FlowKey is null);
}
