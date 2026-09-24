# Contract: Retry API

**Feature**: `068-honest-retry-offer-card` | **Status**: Design

Defines how a failed turn is re-attempted. Modelled directly on specs/045's [suggested-actions-api.md](../../045-conversational-agent-runtime/contracts/suggested-actions-api.md), which already solves the same trust problem.

## 1. Request

`ChatRequest` gains one optional member, alongside the existing optional `SelectedAction`:

```csharp
public sealed record ChatRequest(
    Guid ChatId,
    IReadOnlyList<ChatMessageDto> Messages,
    Guid ProviderId,
    Guid ModelId,
    GenerationParametersDto? GenerationParameters = null,
    SelectedActionRequest? SelectedAction = null,
    RetryRequest? Retry = null);

public sealed record RetryRequest(Guid FailedMessageId);
```

Additive and backward-compatible — existing clients omit it.

### The security property

`RetryRequest` carries **only a message id**. Every parameter of the replayed action is read server-side from that message's persisted `TurnOutcomeJson`. The client cannot name a capability, supply arguments, or redirect the target.

This mirrors `SelectedActionResolver`, which never trusts client-supplied arguments for exactly this reason: an action dispatch that accepts client parameters is a privileged endpoint with client-controlled input.

`SelectedAction` and `Retry` are mutually exclusive; both present is a 400.

## 2. Resolution

`RetryTargetResolver.ResolveAsync(Guid userChatId, Guid failedMessageId, CancellationToken)` → `RetryTarget`.

1. Load the message, scoped to the caller's chat. A message in another user's chat resolves as not found — never as a permission error that confirms it exists.
2. Read its `TurnOutcomeJson`. Null → unknown (400).
3. Find the failed attempt. None → unknown (400).
4. If the attempt records `succeeded: true` → refuse (FR-015); the user is told it already succeeded and asked whether to run it again.
5. Re-validate the recorded target against current workspace state. No longer resolvable → stale (409), with the reason (FR-014).
6. Return the attempt for replay.

### Errors

Reuses the existing exception vocabulary rather than inventing a parallel one:

| Condition | Exception | Status |
|---|---|---|
| No such message, no outcome, or no failed attempt | `ConversationActionUnknownException` | 400 |
| Recorded target no longer resolves | `ConversationActionStaleException` | 409 |
| Capability currently unavailable | `ConversationActionUnavailableException` | 409 |
| Already succeeded | `ConversationActionUnknownException` (refusal path) | 409 |

All surface to the user in the chat as Problem Details (FR-014) — never a silent no-op, and never an unexplained repeat of the original failure notice.

## 3. Dispatch

A resolved retry **bypasses the turn router** and enters the act path directly with the recorded attempt. This is the whole point of the explicit affordance (FR-013): recovery must not depend on the router interpreting typed language, since a router failure is what produced the original defect.

Typed retry ("try again", "retry that") still goes through the router, which now sees the bounded outcome summary and can route it (FR-008, FR-009).

### Transcript effects

| Effect | Behaviour |
|---|---|
| New assistant turn | **Appended**, with its own `TurnOutcomeJson` (FR-013a) |
| Original failed turn | **Left intact and visible** above it (FR-013a, SC-012) |
| User message | **None inserted** (FR-013b) |

This is the one deliberate divergence from selected-action dispatch, which *does* persist a user message carrying the chosen row's label. Retry must not, because the user did not type it.

The retry's outcome statement must be distinguishable from the original attempt's (FR-011, SC-005) — a second identical failure notice with no indication anything was attempted is the spec's "provider still unavailable at retry time" edge case.

## 4. Client affordance

A retry control on any assistant message whose `turnOutcome` records a failed attempt. It belongs in the existing specs/046 action row alongside Replay/Stop/Copy.

| State | Behaviour |
|---|---|
| Visible | Message has a `turnOutcome` with `succeeded: false` on at least one attempt |
| Hidden | No outcome, verdict `AnsweredInWords`, or all attempts succeeded |
| Activating | Disabled with a busy state; failures surface as visible feedback, never a console log |

Per the project's error-handling rules, the activation handler must be awaited and its rejection path must reach the user through visible UI feedback.

## 5. Ambiguity

When a typed retry cannot be resolved to one specific prior action — two different actions failed earlier — the system asks which the user means rather than selecting one silently (FR-012). When it does pick the most recent failure, it names which action it retried.
