namespace AskLucy.Domain.Conversations;

/// <summary>
/// contracts/suggested-actions-api.md §1 <c>409 conversation-action-stale</c> — the referenced
/// offer is not the newest unanswered one in its chat, is not in this chat at all, or no longer
/// carries the selected row. A selection always names a specific past offer by message id; once a
/// newer message has answered or superseded it, running it would act on a decision the
/// conversation has already moved past.
/// </summary>
public sealed class ConversationActionStaleException(string message) : Exception(message);

/// <summary>
/// contracts/suggested-actions-api.md §1 <c>409 conversation-action-unavailable</c> — the row
/// still exists in the offer, but a freshly built turn context no longer satisfies its
/// precondition (FR-028). Distinct from <see cref="ConversationActionUnknownException"/>: this is
/// a capability that exists but cannot run right now, not one that never existed.
/// </summary>
public sealed class ConversationActionUnavailableException(string message) : Exception(message);

/// <summary>
/// contracts/suggested-actions-api.md §1 <c>400 conversation-action-unknown</c> — the row's key
/// matches no registered capability (or, until specs/045 Phase 6 registers flow definitions, names
/// a flow variant at all). A client-side or protocol error rather than a timing conflict, hence
/// 400 rather than 409.
/// </summary>
public sealed class ConversationActionUnknownException(string message) : Exception(message);
