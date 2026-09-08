using AskLucy.Application.Agents.Tools;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>How long a capability takes, which decides whether it gets its own announcement (FR-005a).</summary>
public enum CapabilityDuration
{
    /// <summary>Sub-second. Runs inline; no announcement of its own when invoked standalone.</summary>
    Brief,

    /// <summary>A few seconds. Announced with a pending label.</summary>
    Noticeable,

    /// <summary>Tens of seconds. Announced, and never started unless asked for or accepted.</summary>
    /// <remarks>Named Extended rather than Long because CA1720 flags identifiers that collide with a type name.</remarks>
    Extended,
}

/// <summary>
/// An <see cref="IAgentTool"/> that Lucy may invoke during a conversation and may offer as a next
/// step (specs/045 FR-010 – FR-014, contracts/conversation-capability.md).
///
/// <para>
/// <b>A separate interface rather than three more members on <see cref="IAgentTool"/>.</b> That
/// type has a dozen implementers, most of which are not conversational at all; widening it would
/// break every one and force meaningless user-facing labels onto tools no user will ever see.
/// Opting in keeps it an ISP-clean extension: a tool becomes conversational by implementing one
/// more interface, and the orchestrator never changes (FR-012).
/// </para>
///
/// <para>
/// <b>Three tiers of disclosure</b> (research.md D13, modelled on Agent Skills). Tier 1 —
/// <see cref="IAgentTool.Description"/> plus <see cref="WhenToUse"/> plus
/// <see cref="ArgumentHint"/> — is shown to the deciding model every turn and must stay compact.
/// Tier 2, <see cref="UsageGuidance"/>, is read only once this capability is chosen. Tier 3 —
/// <see cref="IAgentTool.InputSchemaJson"/>, permissions, risk — reaches <b>no model at all</b>,
/// which is what makes the grounder's validation an independent check rather than something the
/// model could shape its arguments around.
/// </para>
/// </summary>
public interface IConversationCapability : IAgentTool
{
    // ---- Tier 1: index. Shown to the deciding model every turn. ----

    /// <summary>
    /// When this capability is the right choice, in third person, with the concrete words a user
    /// actually says.
    /// <para>
    /// Pairs with <see cref="IAgentTool.Description"/> ("what it does") to form the complete index
    /// entry. Both halves are required: a model told only a capability's <i>purpose</i> has
    /// nothing to match a request against, which is the single most common reason routing picks
    /// the wrong tool.
    /// </para>
    /// </summary>
    /// <example>
    /// "Use when the user asks to see, find, locate or navigate to a named real-world place, or
    /// mentions a site, park, building or address they want shown on the map."
    /// </example>
    string WhenToUse { get; }

    /// <summary>Plain-language hint at what this needs — "the place name". Never a schema; schemas are Tier 3.</summary>
    string ArgumentHint { get; }

    // ---- Tier 2: guidance. Loaded only when this capability is selected. ----

    /// <summary>
    /// How to use this capability well, what its outputs mean, and how to report the result.
    /// <para>
    /// Match specificity to fragility: a fragile, expensive capability gets a prescribed sequence,
    /// an open-ended one gets heuristics. Keep it short — once loaded it competes with the
    /// conversation for context, so document only what the model cannot already know.
    /// </para>
    /// </summary>
    string UsageGuidance { get; }

    // ---- User-facing. ----

    /// <summary>Short user-facing name for an offer row. The only part spoken aloud (FR-044).</summary>
    string Label { get; }

    /// <summary>One line beneath the label. Rendered, never spoken (FR-044).</summary>
    string OfferDescription { get; }

    /// <summary>
    /// The acknowledgement shown before this capability runs — "OK, let me find it first."
    /// Templated, never model-generated (research.md D15), so the first thing the user sees is
    /// off the critical path and survives a failed decision step.
    /// </summary>
    string AcknowledgementTemplate { get; }

    // ---- Availability, offerability, cost. ----

    /// <summary>
    /// Whether this <b>could run</b> against the given turn state. Gates the Tier 1 index and
    /// invocation (FR-014).
    /// <para>
    /// Pure and synchronous by contract — every input is already on <see cref="TurnContext"/>. An
    /// implementation that needs I/O to answer is describing the wrong precondition.
    /// </para>
    /// </summary>
    bool IsAvailable(TurnContext context);

    /// <summary>
    /// Whether running this would <b>plausibly be wanted next</b>. Gates the offer only (FR-025b),
    /// and is a strictly stronger test than <see cref="IsAvailable"/>.
    /// <para>
    /// The two are separate questions, and conflating them produces a product that nags: a
    /// capability that is always invocable is not thereby always worth suggesting. Most
    /// capabilities here are never offerable — they exist to be used when asked for, not
    /// advertised. Defaults to <see cref="IsAvailable"/> only where the two genuinely coincide.
    /// </para>
    /// </summary>
    bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => IsAvailable(context);

    /// <summary>Roughly how long a successful invocation takes; drives the announcement rule (FR-005a).</summary>
    CapabilityDuration ExpectedDuration { get; }
}
