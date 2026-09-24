# Phase 1 Data Model: Honest Turn Outcomes, Real Retry, and a Readable Offer Card

**Feature**: `068-honest-retry-offer-card` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

One new persisted column, one new serialized document shape, and two derived projections. No new tables and no new aggregates.

## 1. `Message.TurnOutcomeJson` (new column)

The single authority required by FR-004b.

| Property | Value |
|---|---|
| Entity | `AskLucy.Domain.Chats.Message` |
| Column | `TurnOutcomeJson` |
| Type | `nvarchar(max)`, **nullable** |
| Written by | `AppendMessageCommand`, in the same transaction as the message |
| Precedent | `Message.SuggestedActionsJson` — same shape, same lifecycle, same nullability |

Nullable because every message persisted before this migration has no outcome, and because user messages never carry one. A null outcome on an assistant message is the FR-002c condition: the claim gate suppresses action claims rather than trusting them.

**Migration**: additive, nullable, no backfill. Historical assistant messages keep `null` and are treated as "outcome unknown" — they are never read as successes.

## 2. `RecordedTurnOutcome` (serialized document)

The value serialized into `TurnOutcomeJson`. Lives in `AskLucy.Application`; the Application layer never references EF Core, so this is a plain record serialized at the persistence boundary.

**Why not just `TurnOutcome`**: that name is already taken. `AskLucy.Application.Conversations.Capabilities.TurnOutcome` (`TurnContext.cs:78`) is a specs/045 per-turn offer-suppression record — `WasInvokedThisTurn`, `WasOfferedAndIgnored` — consumed by `ConversationCapabilityCatalog.OfferableFor`. Different concept, adjacent namespace, and `ConversationTurnOrchestrator` imports both. Do not "simplify" this back to `TurnOutcome`.

```
RecordedTurnOutcome
├── Verdict            : TurnVerdict        — required
├── Attempts           : ActionAttempt[]    — may be empty
├── FailureReason      : string?            — set when the turn failed before completing
└── RecordedAtUtc      : DateTimeOffset     — required
```

### `TurnVerdict` (enum)

The four states FR-005 enumerates. Serialized as a **string**, per the project-wide enum converter.

| Value | Meaning |
|---|---|
| `AnsweredInWords` | The turn replied without attempting any workspace action. |
| `Acted` | At least one action was attempted; see `Attempts` for per-action results. |
| `FailedBeforeCompleting` | The turn did not reach its normal end (the mid-stream catch path). |

`Acted` deliberately carries no aggregate pass/fail. FR-006 requires partial success be reported per part, so a single verdict covering all attempts would destroy the information the requirement exists to preserve. The "acted and succeeded" / "acted and failed" distinction in FR-005 is read from `Attempts`, not from the verdict.

### `ActionAttempt`

```
ActionAttempt
├── Kind           : string   — capability kind, matching the suggested-action vocabulary
├── Key            : string?  — capability key where the kind is parameterised
├── TargetLabel    : string?  — human-readable target ("Al Safa Park 2"), for the routing summary
├── ArgumentsJson  : string   — the resolved arguments, server-side values only
├── Succeeded      : bool     — the action's own reported result
└── FailureReason  : string?  — set when Succeeded is false
```

`Succeeded` is the sole source for any success statement (FR-001). `ArgumentsJson` holds **server-resolved** arguments, never client-supplied input — this is what makes replay safe under Decision 4.

### Validation rules

- `Verdict == AnsweredInWords` → `Attempts` MUST be empty.
- `Verdict == Acted` → `Attempts` MUST be non-empty.
- `Succeeded == false` → `FailureReason` MUST be non-empty (FR-014 needs a reason to surface).
- `Verdict == FailedBeforeCompleting` → `FailureReason` MUST be non-empty; `Attempts` MAY be non-empty, covering the spec's partial-success edge case where a turn resolved a location and then failed outlining its boundary.

## 3. `RecentTurnOutcomeSummary` (derived projection, not persisted)

The bounded routing context from Decision 3, fed to `TurnDecider` (FR-009).

```
RecentTurnOutcomeSummary
└── Turns : RecentTurnLine[]   — at most 3, newest last

RecentTurnLine
├── Kind         : string?
├── TargetLabel  : string?
└── Verdict      : string      — "succeeded" | "failed" | "answered-only" | "did-not-complete"
```

Bounded at a constant 3 (FR-009a, SC-009). Projected from `TurnOutcomeJson` only — never from message prose (FR-009b). An empty summary produces a prompt byte-identical to today's (FR-009c).

## 4. `RetryTarget` (derived projection, not persisted)

Resolved server-side by `RetryTargetResolver` from the most recent message-attached outcome carrying a failed attempt.

```
RetryTarget
├── SourceMessageId : Guid           — the failed assistant message being retried
└── Attempt         : ActionAttempt  — the failed attempt, replayed as recorded
```

Mirrors `ResolvedSelectedAction(Row, OfferingMessageId)`. The client supplies only a message id; every parameter comes from the persisted outcome (FR-010).

**Resolution rules**:

- No failed attempt in the conversation → `ConversationActionUnknownException` (400).
- The named message's outcome records `Succeeded == true` → refuse (FR-015); the user is told it already succeeded.
- More than one distinct failed action among recent turns and no message id supplied → ask rather than guess (FR-012, and the spec's ambiguous-target edge case).
- The recorded target no longer resolves → `ConversationActionStaleException` (409), surfaced with its reason (the changed-preconditions edge case).

## 5. Entity relationships

```
Chat 1──* Message
              └── TurnOutcomeJson  (0..1, assistant messages only)
                        │
                        ├──> RecentTurnOutcomeSummary  (last 3, for routing)
                        ├──> claim gate authority       (per-turn, for verification)
                        └──> RetryTarget                (most recent failure, for retry)

AgentExecution ──(chat id only, advisory)──> Chat
```

The audit trail is intentionally drawn as a side branch. It is extended to cover every turn (FR-004e) but is never read as the authority (FR-004f) — it carries no message id, so it cannot attribute an outcome to the message a user is looking at.

## 6. Mapping requirements to the model

| Requirement | Satisfied by |
|---|---|
| FR-001, FR-003 | `ActionAttempt.Succeeded` as the only success source |
| FR-004a, FR-004b | `Message.TurnOutcomeJson`, written transactionally |
| FR-004c | `TurnVerdict.FailedBeforeCompleting`, written from the mid-stream catch |
| FR-004d | Same transaction as the message — no partial state to detect |
| FR-004e, FR-004f | `AgentExecution`, extended but advisory |
| FR-005 | `TurnVerdict` + `Attempts` read together |
| FR-006 | Per-attempt `Succeeded`, with no aggregate verdict |
| FR-007 | `FailedBeforeCompleting` makes the failure notice structurally distinguishable from prose |
| FR-009, FR-009a–c | `RecentTurnOutcomeSummary`, constant-size, outcome-derived |
| FR-010, FR-013a | `RetryTarget.Attempt.ArgumentsJson` replayed as recorded |
| FR-012, FR-015 | `RetryTargetResolver` resolution rules |
| FR-014 | `FailureReason` required whenever `Succeeded` is false |

The offer-card requirements (FR-016 – FR-025) are presentation-only and introduce no data-model change; they are covered in [contracts/offer-card-presentation.md](./contracts/offer-card-presentation.md).
