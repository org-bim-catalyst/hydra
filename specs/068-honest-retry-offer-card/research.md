# Phase 0 Research: Honest Turn Outcomes, Real Retry, and a Readable Offer Card

**Feature**: `068-honest-retry-offer-card` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

This document resolves the six design unknowns the specification leaves open. No `[NEEDS CLARIFICATION]` markers were raised during `/speckit-specify` or `/speckit-clarify`; the unknowns below are design questions the clarified requirements imply, not gaps in the requirements themselves.

## Reproduction: the causal chain behind Defect 1

Established by reading the runtime before proposing anything. Every decision below is anchored to a link in this chain.

1. The AI provider credential used by the turn router was dead.
2. `TurnDecider.DecideAsync` caught the provider exception and returned `TurnDecision.Degraded` — it never throws, by design.
3. `Degraded` sets `IsFastPath`, so `ConversationTurnOrchestrator.RunFastPathAsync` ran. That path streams the chat model with full conversation history.
4. The same dead credential threw again, this time mid-stream, escaping to `AiController`'s catch at line 265.
5. The catch wrote `" Something went wrong partway through and I couldn't finish. Please try again."` into the SSE stream and persisted it via `PersistAssistantMessageAsync` as **ordinary assistant prose** — indistinguishable in the `Messages` table from a normal reply.
6. `TurnRecorder.RecordAsync` sits at `ConversationTurnOrchestrator.cs:248`, *after* the streaming loops. The mid-stream throw skipped it, so the audit trail has no record of the turn at all.
7. On the next turn, `TurnDecider` received **only** the bare string `"try again"` — `TurnDecider.cs` builds its message list from `TurnDecisionPrompt.Build(...)` plus the single latest user message, with no history. An unroutable fragment degrades to `answer`.
8. `answer` → fast path → the chat model saw a transcript whose last assistant message was plain prose about something going wrong, and composed the plausible continuation: *"I've shown you Al Safa Park 2."*

Two independent root causes fall out: **the turn's real outcome was never recorded anywhere durable** (steps 5–6) and **the router cannot see prior turns** (step 7). The fabrication in step 8 is the symptom of both.

A third finding shapes the design: `ReplyScopePromptFraming.BuildSystemMessage()` **already** instructs the model *"The application appends its own confirmation sentences for actions it performed… Do not write those yourself."* The model wrote one anyway. **Prompt-only guidance is demonstrably insufficient for this guarantee** — that is the evidence base for Decision 1 choosing an enforcement mechanism over stronger wording.

---

## Decision 1 — Verification without giving up streaming

**The tension** (recorded as an edge case in the spec, and the hardest part of this feature): FR-002a requires every composed reply be checked against the recorded outcome and corrected *before the user reads it*. But replies stream token-by-token over SSE, and voice output reads them aloud as they arrive. Buffering each reply to completion would cost exactly the responsiveness the platform streams for, and would violate SC-001b.

**Decision**: Two layers — structural prevention first, deterministic sentence-gated enforcement as the guarantee.

**Layer 1 — the model is not the source of action confirmations.** Action confirmations continue to be appended by the application from the recorded outcome. What changes is that the fast path's system framing now carries the structured outcome summary from Decision 3, stating explicitly which recent actions did *not* succeed. This removes the model's motive to fabricate rather than merely forbidding it again.

**Layer 2 — a deterministic claim gate in the stream, buffering at sentence granularity.** A filter sits at the single content-delta write site (`AiController.cs:184`). It accumulates deltas until a sentence boundary, then:

- A sentence containing no workspace-action claim is released immediately.
- A sentence containing an action claim is matched against the turn's recorded outcome. If the outcome records a matching success, it is released unchanged (FR-002a's false-positive protection, and the spec's "verification disagrees with a correct reply" edge case). If it does not, the sentence is **withheld and replaced** with a correction composed from the recorded outcome (FR-002b — never a blank or a truncated sentence).

**Rationale**:

- **It satisfies "before the user sees it" literally.** The withholding happens at the sentence that would carry the false claim, not after the reply completes. Nothing false is ever written to the wire.
- **It costs nothing on the common turn.** Maximum added latency is one sentence, and only for sentences that trip the claim patterns. Ordinary conversational replies stream as they do today — SC-001b.
- **Sentence granularity is already the system's natural unit.** `TextToSpeechStreamer` is documented as operating "sentence-by-sentence", so the gate introduces no new segmentation concept and no new desync between text and audio.
- **It is deterministic, with no provider dependency.** This is decisive: the scenario that triggered the whole defect was *a dead provider*. A verification step that needs an LLM call would be unavailable in precisely the situation it exists to protect against, and would add a full round trip to every turn.

**Alternatives considered**:

| Alternative | Rejected because |
|---|---|
| Buffer the whole reply, verify, then send | Kills streaming on every turn to protect the rare one; voice output would wait for the full reply. Fails SC-001b outright. |
| Post-hoc correction (send, then retract) | The user has already read — and heard — the false claim. Fails FR-002a's "before the user reads it" and does nothing for a voice-only user. |
| A second LLM call to judge the reply | Unavailable exactly when needed (dead provider), adds a round trip per turn, and makes a non-negotiable guarantee probabilistic. |
| Stronger prompt instructions only | Already tried and already failed in production — see the `ReplyScopePromptFraming` finding above. |

**FR-002c fallback**: when the gate cannot run — no recorded outcome retrievable for the turn — it suppresses action-claim sentences rather than passing them through unverified, and the turn proceeds with a claim-free reply.

---

## Decision 2 — Where the turn outcome lives

**Decision**: Persist a nullable `TurnOutcomeJson` column on `Message`, written in the same `AppendMessageCommand` transaction that persists the assistant message. The existing `AgentExecution` audit trail is extended to cover every turn, but stays advisory.

**Rationale**:

- `Message.SuggestedActionsJson` is the exact precedent: structured per-turn data, serialized as JSON, attached to the message it describes. Following it costs one nullable column and no new aggregate.
- FR-004b demands a *single* authority for reply composition, verification and retry. Attaching the outcome to the message makes that authority unambiguous, and it survives a page reload for free (SC-001c) because the transcript already loads messages.
- Writing it inside the existing persistence call makes FR-004d ("surface a failure to persist the outcome") automatic — the message write and the outcome write succeed or fail together. There is no state in which a message exists without its outcome.
- The audit trail cannot be the authority. The user asked this directly during clarification, and the answer is **no, not as it stands**, for three concrete reasons: `RecordAsync` is unreachable on the failure path (chain step 6); `AgentExecution.Create(..., userChatId, ...)` carries a chat id but **no message id**, so outcomes cannot be attributed to the message the user is looking at; and `TurnRecorder` deliberately swallows its own exceptions ("isolation, not suppression"). Making it authoritative would mean rebuilding all three guarantees in a second store and joining across them.

**Alternatives considered**: audit trail alone (above); a separate `RecordedTurnOutcome` table keyed by message id (a join and a new aggregate buying nothing the column does not, and it reintroduces the two-write consistency problem FR-004d exists to prevent); in-memory cache only (does not survive reload, fails SC-001c).

**Follow-through for the failure path**: the mid-stream catch in `AiController` must record a failure outcome before it returns (FR-004c), and `TurnRecorder.RecordAsync` must be reachable from that path (FR-004e) while keeping its swallow-and-log behaviour (FR-004f).

---

## Decision 3 — What the router receives

**Decision**: `TurnDecider` takes a third input — a bounded `RecentTurnOutcomeSummary` covering the last **3** turns, each rendered as one compact line: intent, capability, target, verdict. Not raw prose.

**Rationale**:

- Directly fixes chain step 7. `"try again"` alone is unroutable; `"try again"` plus *"previous turn: set-location, target 'Al Safa Park 2', FAILED"* is not.
- Fixed at 3 turns, so the prompt does not grow with conversation length (FR-009a, SC-009). Raw history would grow unboundedly and reintroduce per-turn cost.
- Outcome-derived rather than prose-derived (FR-009b) — this is what stops the router being misled by the failure notice's own wording, which is what happened in production.
- When a conversation has no recorded outcomes, the summary is empty and the prompt is byte-identical to today's (FR-009c). No cost, no behaviour change, no regression risk on first turns.

**Alternatives considered**: full conversation history (unbounded cost, and prose is the untrustworthy signal — it is how the failure notice got read as an answer); last turn only (breaks the spec's "two different actions failed" edge case, since the router needs to see enough to know when to ask); last N *messages* rather than N outcomes (mixes prose back in).

---

## Decision 4 — How retry dispatches

**Decision**: Mirror `SelectedActionResolver` exactly. A `RetryTargetResolver` reads the most recent message-attached outcome in the chat carrying a failed retryable action and returns its recorded kind, key and arguments. Dispatch bypasses `TurnDecider` and goes straight to the act path.

**Rationale**:

- `SelectedActionResolver` is a proven precedent for "the client names a message, the server resolves everything else." Its signature — `ResolveAsync(Guid userChatId, Guid offeredByMessageId, ...)` returning `ResolvedSelectedAction(Row, OfferingMessageId)` — already demonstrates the security property this needs: it **never trusts client-supplied arguments**. Retry inherits that by taking only a message id from the client and reading the parameters from the persisted outcome (FR-010).
- Bypassing the router removes the failure mode entirely for the explicit affordance (FR-013): recovery no longer depends on phrasing something the router can interpret. Typed retry still routes via Decision 3, so both paths in the spec are covered.
- Its three existing exception types map one-to-one onto the retry error cases: stale (409), unknown (400), unavailable (409) — matching the spec's "preconditions have changed" and "ambiguous target" edge cases without inventing a new error vocabulary.

**One deliberate divergence**: selection dispatch persists a user message carrying the chosen row's label. Retry **must not** (FR-013b — no message in the user's voice they did not type). The retry appends only a new assistant turn with its own outcome, leaving the failed turn visible above it (FR-013a, SC-012).

**Transport**: `ChatRequest` gains an optional `RetryRequest(Guid FailedMessageId)`, alongside the existing optional `SelectedAction`. Additive and backward-compatible.

**Alternatives considered**: client resubmits the original user text (loses the original parameters, re-enters the router, re-runs RAG/memory framing unnecessarily); client sends the action parameters (trusts client input for a privileged dispatch — rejected on the same grounds `SelectedActionResolver` rejects it); replaying the whole original turn (duplicates the user message, violating FR-013b).

---

## Decision 5 — Offer card width

**Decision**: Remove both caps. `MessageBubble`'s `maxWidth: '75%'` becomes conditional on the message carrying an offer, and `SuggestedActionCard`'s own `maxWidth: '75%'` is removed outright.

**Rationale**:

- The two caps **compound**: 75% of 75% ≈ 56% of the panel, which is precisely the reported symptom. Removing only one leaves the card at 75% and still wrapping. Clarification Q4 chose both, overriding the conditional-only recommendation.
- Conditioning on offer presence rather than on content length (FR-017) avoids a measurement pass and gives a stable, predictable rule: offer-carrying bubbles are wide, ordinary replies keep their reading measure (SC-006a).
- The option row's stacking is a separate fix from width — at ~56% even a short label stacks. The control, label and description move onto a shared horizontal band (FR-018), with a stacked fallback at the narrowest supported width (FR-021).

**Accepted trade-off, recorded in FR-017a**: reply prose in an offer-carrying bubble also renders full width and loses its reading measure. The user chose this explicitly.

**Alternatives considered**: widen the card only (the bubble still clamps it — the compounding point above); widen conditionally on measured content (a layout measurement pass plus reflow, for a rule users cannot predict); render the card outside the bubble (breaks the message-grouping model and the history rendering in FR-020).

---

## Decision 6 — What voice speaks for an offer turn

**Decision**: Replace the label enumeration in `ChatPage.tsx` (lines ~435–465) with a short localized cue.

The current code speaks the reply, then `${reply.question} ${labels.join(', ')}.` — the question plus every non-decline option label. That becomes the reply plus a cue such as *"I've put some choices on screen."*

**Rationale**:

- Single, well-localized change; descriptions, keys and arguments are already excluded, so only the question and labels need dropping (FR-023).
- The cue is what keeps a voice-only user informed that an offer exists (FR-024, and the spec's "voice-only user receives an offer" edge case) — silence would trade one failure for another.
- Appending the cue to the reply already being spoken, rather than interrupting, falls out of the existing `speech.then(...)` sequencing.
- **This does not reduce accessibility.** TTS is not a screen reader; the card's DOM is untouched, so the full question, options and descriptions remain reachable (FR-025, SC-008). FR-025 exists specifically so this is not mistaken for an a11y regression in review.

**Alternatives considered**: speak nothing extra (a voice-only user would not know a choice is waiting); speak the question but not the labels (still the longest part of the card, and SC-011 measures the whole card); make it a user preference (a setting for a defect fix — YAGNI, and the constitution's Simplicity First).

---

## Cross-cutting notes

**Constitution §2 VIII, No Silent Failures.** Per the standing project reading, this principle is about *capturing* every failure for diagnosability — it is not violated by choosing what an end user sees. That reading is what permits FR-002b's correction behaviour: the false claim is replaced for the user while the underlying failure is fully logged and recorded in the outcome.

**Clean Architecture.** The claim gate and the outcome model belong in `AskLucy.Application`; `AskLucy.Web` only wires the gate into the SSE write. The Application layer never references EF Core, so the outcome is a domain-shaped record serialized at the persistence boundary.

**Testing.** All six decisions are unit-testable without a provider: the gate is deterministic, the summary is a pure projection, and the retry resolver mirrors an already-tested precedent.
