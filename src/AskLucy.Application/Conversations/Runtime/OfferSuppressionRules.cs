using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>Why the offer step was skipped entirely — no model call, no <c>__ACTIONS__</c> event (specs/045 FR-025a).</summary>
public enum OfferSuppressionReason
{
    /// <summary>Not suppressed — the offer step runs.</summary>
    None,

    /// <summary>FR-025a.1 — the turn answered in words and invoked nothing. Answering a question is not a reason to ask what to do next.</summary>
    AnsweredOnly,

    /// <summary>FR-025a.2/.4 — nothing is offerable for the resulting state, including because everything offerable was already offered last turn and ignored (that history is folded into <see cref="ConversationCapabilityCatalog.OfferableFor"/> itself).</summary>
    NothingOfferable,

    /// <summary>FR-025a.3 — a decline is an answer, and re-asking is nagging.</summary>
    UserDeclinedLastOffer,

    /// <summary>FR-025a.5/FR-032 — the user has suggested actions turned off.</summary>
    SuggestedActionsDisabled,
}

/// <summary>
/// The five conditions under which the offer step must not run at all (specs/045 FR-025a,
/// contracts/turn-stream.md §2). Pure and synchronous so each rule can be asserted in isolation,
/// without a model double standing in for "no call happened" — the point every rule here exists to
/// guarantee.
/// </summary>
public static class OfferSuppressionRules
{
    /// <summary>
    /// Evaluated in cheapest-first order: a disabled preference or a plain answer never needs to
    /// touch the capability catalog at all.
    /// </summary>
    public static OfferSuppressionReason Evaluate(
        TurnIntent intent,
        TurnContext context,
        TurnOutcome outcome,
        ConversationCapabilityCatalog catalog,
        bool suggestedActionsEnabled,
        IReadOnlyList<FlowVariantOfferCandidate>? flowVariantCandidates = null)
    {
        if (!suggestedActionsEnabled)
        {
            return OfferSuppressionReason.SuggestedActionsDisabled;
        }

        // Deliberately keyed on the decided intent, not on "no beats ran": TurnIntent.Suggest also
        // takes the wordsonly path today (TurnDecision.IsFastPath is true for it too), but a
        // "do you know X?" turn is exactly the case the offer step exists for (research.md D18) —
        // only a genuine TurnIntent.Answer skips it.
        if (intent == TurnIntent.Answer)
        {
            return OfferSuppressionReason.AnsweredOnly;
        }

        if (outcome.UserDeclinedLastOffer)
        {
            return OfferSuppressionReason.UserDeclinedLastOffer;
        }

        // specs/045 Phase 6 — a flow variant is worth offering independently of whether any
        // standalone capability is (FR-025b): "do you know X?" can have zero offerable
        // capabilities and still be exactly the case a flow's variants exist for.
        if (catalog.OfferableFor(context, outcome).Count > 0 || (flowVariantCandidates?.Count ?? 0) > 0)
        {
            return OfferSuppressionReason.None;
        }

        return OfferSuppressionReason.NothingOfferable;
    }
}
