using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Chats;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// What Lucy has to work with this turn (specs/045 FR-011, data-model.md §8).
///
/// <para>
/// Captured once, before the decide step, and passed to every capability's availability check.
/// A snapshot rather than a live query for two reasons: availability must not shift underneath
/// a single turn's reasoning, and <see cref="IConversationCapability.IsAvailable"/> is required
/// to be pure and synchronous — a predicate that could hit the database would make "what can
/// Lucy do right now" an unbounded amount of I/O on the critical path.
/// </para>
///
/// <para>
/// Dispatch (FR-028) deliberately builds a <b>fresh</b> context rather than reusing this one,
/// because a selection may arrive turns later, against state that has moved on.
/// </para>
/// </summary>
/// <param name="UserId">The user this turn belongs to; null only for an unauthenticated path that should never reach a capability.</param>
/// <param name="UserChatId">The conversation.</param>
/// <param name="ActiveLocation">The place confirmed for this chat, if any. The precondition most capabilities gate on.</param>
/// <param name="ActiveBoundary">The site outline already drawn, if any. Drives the non-redundancy rule.</param>
/// <param name="AttachedKnowledgeBaseIds">Knowledge bases linked to this conversation.</param>
/// <param name="HasAttachedDocuments">Whether the conversation has documents to analyse.</param>
/// <param name="IsMemoryAvailable">Whether the memory subsystem can serve this turn.</param>
/// <param name="OpenPanelTypeKeys">Visual panels already open, so a capability can avoid opening a duplicate.</param>
/// <param name="GrantedPermissions">
/// What the user is authorized for. Enforced by the catalog rather than by each capability
/// (FR-011 rule 3), so a newly written capability cannot forget the check.
/// </param>
/// <param name="SubscriptionTier">The tier gating entitlement, alongside permissions.</param>
public sealed record TurnContext(
    string? UserId,
    Guid UserChatId,
    ActiveSiteLocation? ActiveLocation,
    ActiveSiteBoundary? ActiveBoundary,
    IReadOnlyList<Guid> AttachedKnowledgeBaseIds,
    bool HasAttachedDocuments,
    bool IsMemoryAvailable,
    IReadOnlyList<string> OpenPanelTypeKeys,
    IReadOnlySet<AgentToolPermission> GrantedPermissions,
    string? SubscriptionTier)
{
    /// <summary>True when a place has been confirmed for this chat — the precondition most viewer work depends on.</summary>
    public bool HasActiveLocation => ActiveLocation is not null;

    /// <summary>
    /// True when the active boundary already describes the active location's site. The
    /// non-redundancy test: outlining a site that is already outlined produces nothing new, so
    /// the capability is neither available nor worth offering.
    /// </summary>
    public bool IsBoundaryCurrentForActiveLocation =>
        ActiveLocation is not null &&
        ActiveBoundary is not null &&
        string.Equals(ActiveBoundary.SiteName, ActiveLocation.LocationName, StringComparison.OrdinalIgnoreCase);

    /// <summary>An empty context, for the fast path and for tests that care about a single field.</summary>
    public static TurnContext Empty(string? userId = null, Guid userChatId = default) =>
        new(userId, userChatId, null, null, [], false, false, [], new HashSet<AgentToolPermission>(), null);
}

/// <summary>
/// What the turn actually did, handed to <see cref="IConversationCapability.IsOfferable"/> so a
/// capability can judge relevance against the thing that just happened rather than against
/// standing state alone (FR-025b rule 5).
/// </summary>
/// <param name="InvokedCapabilityKeys">Capabilities that ran this turn — the basis of the "don't offer what just ran" rule.</param>
/// <param name="ConfirmedLocationThisTurn">True when this turn is the one that established the active location; the moment a boundary offer makes sense.</param>
/// <param name="PreviouslyOfferedKeys">What the last offer contained, so ignored suggestions are not repeated (FR-025a.4).</param>
/// <param name="UserDeclinedLastOffer">FR-025a.3 — a decline is an answer, and re-asking is nagging.</param>
public sealed record TurnOutcome(
    IReadOnlyList<string> InvokedCapabilityKeys,
    bool ConfirmedLocationThisTurn,
    IReadOnlyList<string> PreviouslyOfferedKeys,
    bool UserDeclinedLastOffer)
{
    public static readonly TurnOutcome None = new([], false, [], false);

    public bool WasInvokedThisTurn(string capabilityKey) =>
        InvokedCapabilityKeys.Contains(capabilityKey, StringComparer.Ordinal);

    public bool WasOfferedAndIgnored(string capabilityKey) =>
        PreviouslyOfferedKeys.Contains(capabilityKey, StringComparer.Ordinal);
}
