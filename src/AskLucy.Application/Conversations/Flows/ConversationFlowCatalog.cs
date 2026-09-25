using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Runtime;

namespace AskLucy.Application.Conversations.Flows;

/// <summary>
/// One flow variant worth offering, with its arguments already bound (specs/045 FR-051b).
/// <para>
/// Computed by the orchestrator from the decide step's own <c>FlowKey</c>/<c>FlowArgumentsJson</c>
/// — the same place name the flow would have used had it run — never re-invented by the offer
/// step's own model call. A flow has no formal input schema (<see cref="IConversationFlow"/>
/// carries only <see cref="IConversationFlow.ArgumentHint"/>, a hint for a human/model reading it,
/// not a contract to validate against), so letting the offer step's model also propose arguments
/// would leave nothing to ground them against; handing it a fully-formed, already-correct
/// candidate and letting it only choose whether to include it by key sidesteps the problem
/// entirely rather than solving a schema that doesn't exist.
/// </para>
/// </summary>
/// <param name="CompoundKey">The offer row's key: <c>flowKey:variantKey</c> (data-model.md §1).</param>
public sealed record FlowVariantOfferCandidate(
    string CompoundKey,
    string FlowKey,
    string VariantKey,
    string Label,
    string Description,
    string ArgumentsJson);

/// <summary>
/// Resolves which flows Lucy may see or run this turn (specs/045 FR-050-FR-051), mirroring
/// <see cref="ConversationCapabilityCatalog"/>'s own shape exactly.
/// </summary>
public sealed class ConversationFlowCatalog(IEnumerable<IConversationFlow> flows)
{
    public IReadOnlyList<IConversationFlow> AvailableFor(TurnContext context) =>
        [.. flows.Where(f => f.IsAvailable(context))];

    public IConversationFlow? Find(string flowKey) =>
        flows.FirstOrDefault(f => string.Equals(f.Key, flowKey, StringComparison.Ordinal));

    /// <summary>
    /// A decision that acts with one slice, where that slice is exactly the first step of an
    /// available flow, becomes a run of that whole flow. Any other decision is returned unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Live-tested 2026-09-25: the deciding model sometimes answered "show me Dubai Mall" with a
    /// lone <c>resolve_location</c> slice rather than <c>locate_a_place</c>, because both entries'
    /// "when to use" text names the same requests. The place was found but never outlined, and the
    /// same wording outlined the site on the next try. Which one the model picked is a coin toss,
    /// so it is settled here rather than in the prompt.
    /// </para>
    /// <para>
    /// A user who explicitly limits the job ("just find it") is still served: the prompt asks for
    /// the flow with <c>throughStepIndex</c> 0 in that case, and a flow decision is never touched
    /// here. Only a single slice is promoted, because a slice with siblings is feeding another
    /// capability (weather at X, solar analysis of X), not asking for the place to be shown. The
    /// slice's arguments become the flow's input, so this relies on a flow's first step binding
    /// straight from that input, as <see cref="LocateAPlaceFlow"/>'s does.
    /// </para>
    /// </remarks>
    public TurnDecision PromoteLoneFirstStep(TurnDecision decision, TurnContext context)
    {
        if (decision.Intent != TurnIntent.Act || decision.FlowKey is not null || decision.Slices.Count != 1)
        {
            return decision;
        }

        var slice = decision.Slices[0];
        var flow = AvailableFor(context).FirstOrDefault(f =>
            f.Steps.Count > 1 && string.Equals(f.Steps[0].CapabilityKey, slice.CapabilityKey, StringComparison.Ordinal));

        return flow is null
            ? decision
            : new TurnDecision(TurnIntent.Act, [], flow.Key, slice.ArgumentsJson, ThroughStepIndex: null);
    }

    /// <summary>The Tier 1 index entry for one flow — identical shape to a capability's, so the deciding model treats a job and a single capability the same way (research.md D17).</summary>
    public static CapabilityIndexEntry ToEntry(IConversationFlow flow) =>
        new(flow.Key, flow.Description, flow.WhenToUse, flow.ArgumentHint);

    /// <summary>Every variant of <paramref name="flow"/> as a ready-to-offer candidate, bound with <paramref name="flowArgumentsJson"/> (FR-051b).</summary>
    public static IReadOnlyList<FlowVariantOfferCandidate> VariantCandidatesFor(IConversationFlow flow, string flowArgumentsJson) =>
        [.. flow.Variants.Select(v => new FlowVariantOfferCandidate(
            $"{flow.Key}:{v.Key}", flow.Key, v.Key, v.Label, v.OfferDescription, flowArgumentsJson))];
}
