using System.Text.Json;
using AskLucy.Application.Conversations.Capabilities;

namespace AskLucy.Application.Conversations.Flows;

/// <summary>
/// A declared, ordered sequence of capability steps run as a single job (specs/045 FR-050,
/// contracts/capability-flow.md).
/// <para>
/// Registered in DI like a capability: adding a flow is a registration, never a change to the
/// orchestrator. Appears in the Tier 1 index alongside standalone capabilities (as a
/// <see cref="CapabilityIndexEntry"/> — the shapes are identical, so the deciding model sees a
/// flow and a capability the same way and chooses a job, not a pipeline), so the deciding model
/// may select a whole flow as readily as a single capability.
/// </para>
/// <para>
/// Deliberately not the specs/022 workflow engine (research.md D17): that engine's expression
/// language, branching and human-approval gates solve a different problem than "run these three
/// specific, always-sequential steps and narrate them uniformly."
/// </para>
/// </summary>
public interface IConversationFlow
{
    string Key { get; }

    /// <summary>What the whole job achieves, as one outcome — not a list of its steps.</summary>
    string Description { get; }

    /// <summary>When this flow is the right choice. Same authoring rules as a capability's <see cref="IConversationCapability.WhenToUse"/>.</summary>
    string WhenToUse { get; }

    string ArgumentHint { get; }

    /// <summary>Whether this flow can run at all against the current turn state.</summary>
    bool IsAvailable(TurnContext context);

    IReadOnlyList<FlowStep> Steps { get; }

    /// <summary>
    /// Named contiguous prefixes of <see cref="Steps"/>, offered as distinct choices when the
    /// turn's intent was informational (FR-051b, research.md D18). A variant is a whole outcome
    /// the user can want; a step is machinery they should not have to assemble (FR-060).
    /// </summary>
    IReadOnlyList<FlowVariant> Variants { get; }
}

/// <summary>One offerable way to run a flow: its first N steps, under a user-facing name.</summary>
/// <param name="Key">Unique within the flow — combined with the flow's own key as <c>flowKey:variantKey</c> for an offer row (data-model.md §1).</param>
/// <param name="ThroughStepIndex">0-based index of the last step this variant runs, inclusive.</param>
public sealed record FlowVariant(
    string Key,
    string Label,
    string OfferDescription,
    int ThroughStepIndex);

/// <summary>One step of a flow. Ordered; each depends on the step before it (FR-055).</summary>
/// <param name="CapabilityKey">Resolved against <see cref="ConversationCapabilityCatalog"/> at run time — a flow names capabilities by key, never holds a direct reference.</param>
/// <param name="AnnouncementTemplate">Said before this step starts (FR-052). "Now focusing the viewer on it."</param>
/// <param name="BindArguments">
/// Builds this step's arguments from the flow's input and the previous steps' output — simple
/// binding, not an expression language (research.md D17). Returning null means the step cannot
/// proceed and is treated per <see cref="IsRequired"/>.
/// </param>
/// <param name="IsAlreadySatisfied">
/// True when this step's work is already done for the current context, so it is skipped with a
/// brief note rather than repeated (FR-057). The flow continues.
/// </param>
/// <param name="CompletionTemplate">Short past-tense phrase reporting this step's success — "Location found", "Site focused", "Boundary highlighted". Combined with the next step's announcement into one message (FR-053).</param>
/// <param name="IsRequired">False for a step whose failure should not stop the flow. Every step of the initial Locate a place flow is required — each genuinely depends on its predecessor.</param>
/// <param name="SkipTemplate">
/// The brief note read when <see cref="IsAlreadySatisfied"/> holds (FR-057) — "The site is
/// already outlined, so I've left it as it is." A small, deliberate addition beyond
/// contracts/capability-flow.md's own listed shape: without a per-step phrase, a skip note has
/// no natural wording to fall back to beyond a generic "already done," which the contract's own
/// worked example is specific enough to rule out. Null falls back to that generic phrase.
/// </param>
public sealed record FlowStep(
    string CapabilityKey,
    string AnnouncementTemplate,
    Func<FlowStepContext, JsonDocument?> BindArguments,
    Func<FlowStepContext, bool> IsAlreadySatisfied,
    string CompletionTemplate,
    bool IsRequired = true,
    string? SkipTemplate = null);

/// <summary>What one flow step produced, for binding the next step and for the execution record (FR-061).</summary>
/// <param name="CapabilityKey">Which step this is.</param>
/// <param name="Attempted">False for a step never reached — an earlier required step failed, or the run was scoped short of it.</param>
/// <param name="Succeeded">Meaningful only when <see cref="Attempted"/>.</param>
/// <param name="Skipped">True when <see cref="IsAlreadySatisfied"/> held; distinct from a step that failed or was never attempted.</param>
/// <param name="ResultJson">The capability's own successful output, when attempted and succeeded.</param>
/// <param name="Reason">Why this step is in the state it's in — the failure reason, the skip note, or "not attempted" — always populated except for a plain success.</param>
public sealed record FlowStepResult(
    string CapabilityKey,
    bool Attempted,
    bool Succeeded,
    bool Skipped,
    string? ResultJson,
    string? Reason);

/// <param name="Turn">The turn's snapshot, captured once before the flow started — steps needing knowledge from a just-completed sibling step read <see cref="CompletedSteps"/> instead, since this does not change mid-flow.</param>
/// <param name="FlowInput">The flow's own top-level input (e.g. the place name), bound once from the decide step's (or a selected variant's) arguments.</param>
/// <param name="CompletedSteps">Every step run so far this flow, in order — including skipped ones — so a later step can bind from an earlier one's result.</param>
public sealed record FlowStepContext(
    TurnContext Turn,
    JsonDocument FlowInput,
    IReadOnlyList<FlowStepResult> CompletedSteps);
