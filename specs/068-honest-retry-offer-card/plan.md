# Implementation Plan: Honest Turn Outcomes, Real Retry, and a Readable Offer Card

**Branch**: `068-honest-retry-offer-card` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/068-honest-retry-offer-card/spec.md`

## Summary

Lucy told a user she had shown them a location she had never shown. The turn had failed on a dead provider credential, the failure was persisted as ordinary prose, and the next turn's router — which sees only the latest user message — could not route "try again", so the chat model invented a plausible continuation.

The fix makes each turn's **real outcome** a first-class, persisted fact and then uses it three ways: to inform reply composition, to gate what reaches the user, and to replay a failed action on request.

Approach, as resolved in [research.md](./research.md):

1. **Record the outcome.** A nullable `TurnOutcomeJson` column on `Message`, written in the same transaction as the assistant message, following the `SuggestedActionsJson` precedent. It records the verdict and per-action attempts, including for turns that fail before completing. The `AgentExecution` audit trail is extended to cover every turn but stays advisory — it has no message id and so cannot be the authority.
2. **Gate the stream.** A deterministic claim gate at the single content-delta write site buffers at **sentence** granularity: claim-free sentences release immediately, action-claim sentences are checked against the recorded outcome and withheld-and-replaced if unsupported. This resolves the verification-versus-streaming tension without a second LLM call — decisive, because the failure it guards against is a dead provider.
3. **Route with outcomes.** The turn router receives a constant-size, 3-turn summary projected from recorded outcomes, not raw prose. Empty when there is nothing to summarise, so first turns are unchanged.
4. **Retry for real.** A `RetryTargetResolver` mirrors `SelectedActionResolver`: the client sends only a message id, the server reads every parameter from the persisted outcome. Dispatch bypasses the router. A retry appends a new assistant turn and inserts no user message.
5. **Widen the card, quieten the voice.** Remove both compounding 75% width caps for offer-carrying messages, put each option's control/label/description on one band, and replace the spoken enumeration of every option label with a short localized cue.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript 5 / React 19

**Primary Dependencies**: ASP.NET Core, EF Core, MediatR, FluentValidation, Serilog; Vite, MUI, TanStack Query, Zustand

**Storage**: SQL Server. One additive, nullable `nvarchar(max)` column on `Messages`; no backfill.

**Testing**: xUnit + NSubstitute (`AskLucy.Application.Tests`, `AskLucy.Web.Tests`, `AskLucy.Persistence.Tests`); Vitest + Testing Library + axe (ClientApp)

**Target Platform**: ASP.NET Core web app on shared Windows hosting (site4now), SPA client

**Project Type**: Web application — Clean Architecture backend + React SPA

**Performance Goals**: No perceptible added latency on turns that make no action claim; the claim gate never holds text past one sentence boundary. Routing context size constant as a conversation grows from turn 1 to turn 100.

**Constraints**: Replies stream over SSE and voice reads them as they arrive, so verification cannot buffer a whole reply. The verification path must not depend on an AI provider, since provider failure is the scenario it exists to handle. `AskLucy.Application` has no EF Core package reference at all.

**Scale/Scope**: 3 user stories, 41 functional requirements, 17 success criteria. One migration, one new stream event, one new optional request member, two frontend components plus one voice call site.

## Constitution Check

*GATE: checked before Phase 0, re-checked after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| §2 I — Clean Architecture & Dependency Rule | **PASS** | Claim gate, `TurnOutcome` and the routing projection live in `AskLucy.Application`; `AskLucy.Web` only wires the gate into the SSE write. No EF Core reference added to Application — the outcome is a plain record serialized at the persistence boundary. |
| §2 II — SOLID | **PASS** | Gate, resolver and summary projection are each a single responsibility behind an interface. `RetryTargetResolver` parallels `SelectedActionResolver` rather than extending it, so neither grows a mode flag. |
| §2 III — Simplicity First | **PASS** | One nullable column, no new table, no new aggregate. A user-facing setting for voice scope and a separate `TurnOutcome` table were both rejected as YAGNI. |
| §2 V — Dependency Inversion & Testability | **PASS** | The gate is deterministic and provider-free, so the highest-risk logic is unit-testable without a network or an LLM. |
| §2 VIII — No Silent Failures (NON-NEGOTIABLE) | **PASS** | This feature *is* the principle applied. Outcome persistence failure is surfaced (FR-004d); retry failures reach the chat (FR-014); the audit trail's write failure is logged and surfaced to operators without failing the turn (FR-004f). Per the standing project reading, the principle governs capturing failures for diagnosability — replacing a false claim with an accurate one for the user is not a violation. |
| §5 — Database Principles | **PASS** | Additive, nullable, no backfill, no data loss. Code-first migration. |
| §6 — API Standards | **PASS** | Additive optional request member; errors as Problem Details reusing the existing 400/409 exception vocabulary. Enums serialize as strings via the API-wide converter. |
| §7 — UI Principles | **PASS** | WCAG 2.1 AA preserved at every rendered width (FR-022, SC-008). The young-adult female voice persona is untouched — this changes *what* is spoken, not which voice. FR-025 records explicitly that TTS scope ≠ screen-reader scope. |
| §8 — Security | **PASS** | Retry accepts only a message id; every parameter is read server-side from the persisted outcome. Client-supplied capability kinds and arguments are rejected, matching the precedent. Cross-chat message ids resolve as not found. `argumentsJson` is never emitted to the client. |
| §9 — AI Principles | **PASS** | Provider-agnostic throughout; the gate deliberately has no provider dependency. Streaming preserved. |
| §10 — Testing Standards | **PASS** | Unit tests for the gate, resolver and projection; integration tests for the stream event and retry endpoint; frontend tests for width, voice scope and the retry affordance. |

**No violations.** Complexity Tracking is empty.

**Post-design re-check**: unchanged. Phase 1 added no new projects, no new dependencies, and no new persistence surface beyond the single column identified before Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/068-honest-retry-offer-card/
├── spec.md
├── plan.md                              # This file
├── research.md                          # Phase 0 — six decisions + the causal chain
├── data-model.md                        # Phase 1
├── quickstart.md                        # Phase 1 — verification steps
├── checklists/
│   └── requirements.md
├── contracts/                           # Phase 1
│   ├── turn-outcome.md
│   ├── retry-api.md
│   └── offer-card-presentation.md
└── tasks.md                             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Chats/
│       └── Message.cs                                   # + TurnOutcomeJson (nullable)
├── AskLucy.Application/
│   ├── Ai/
│   │   └── ReplyScopePromptFraming.cs                   # carries the outcome summary
│   └── Conversations/Runtime/
│       ├── ConversationTurnOrchestrator.cs              # records outcomes; reachable on failure
│       ├── TurnDecider.cs                               # + bounded outcome summary input
│       ├── TurnDecisionPrompt.cs                        # renders the summary
│       ├── TurnRecorder.cs                              # extended to every turn, still advisory
│       ├── SelectedActionResolver.cs                    # precedent — unchanged
│       ├── TurnOutcome.cs                               # NEW — verdict + attempts
│       ├── TurnOutcomeClaimGate.cs                      # NEW — deterministic sentence gate
│       ├── RecentTurnOutcomeSummary.cs                  # NEW — bounded projection
│       └── RetryTargetResolver.cs                       # NEW — mirrors SelectedActionResolver
├── AskLucy.Persistence/
│   └── (message configuration + migration)              # additive nullable column
└── AskLucy.Web/
    ├── Contracts/AiContracts.cs                         # + RetryRequest on ChatRequest
    ├── Controllers/v1/AiController.cs                   # gate at the delta write; outcome on the
    │                                                    #   failure path; __TURN_OUTCOME__ event
    └── ClientApp/src/features/chat/
        ├── api/aiApi.ts                                 # + __TURN_OUTCOME__ sentinel, turnOutcome
        ├── components/MessageBubble.tsx                 # conditional full width; retry affordance
        ├── components/SuggestedActionCard.tsx           # drop maxWidth; option row on one band
        └── pages/ChatPage.tsx                           # voice cue replaces label enumeration

tests/
├── AskLucy.Application.Tests/Conversations/Runtime/     # gate, summary, resolver
├── AskLucy.Web.Tests/                                   # stream event, retry endpoint, errors
├── AskLucy.Persistence.Tests/                           # TurnOutcomeJson round-trip
└── (ClientApp co-located *.test.tsx)
```

**Structure Decision**: Existing Clean Architecture layout, unchanged. Four new Application-layer types and one migration; everything else is a modification to a file already on the turn path. No new project.

## Implementation Sequencing

The three stories are independently deployable, and US1's foundation is a hard prerequisite for US2.

| Order | Scope | Why here |
|---|---|---|
| 1 | `TurnOutcome` model, column, migration, recording on **both** the normal and mid-stream-failure paths | Everything else reads this. Without it US2 has nothing to replay and the gate has nothing to check. |
| 2 | Claim gate at the delta write site | Delivers US1's guarantee — the reported harm stops here, before any retry work. |
| 3 | Bounded routing summary into `TurnDecider` | Makes typed retry routable (US2); also improves ordinary follow-ups. |
| 4 | `RetryTargetResolver`, `RetryRequest`, dispatch, retry affordance | Completes US2. Depends on 1 and 3. |
| 5 | Card width, option row layout, voice cue | US3 — fully independent; can ship in any order, including first. |

Step 5 has no dependency on 1–4 and could be split into its own PR if the backend work needs more review time.

## Risks

| Risk | Mitigation |
|---|---|
| The claim gate's patterns miss a phrasing, letting a false claim through | Layer 1 removes the motive (the model is told what failed) so the gate is a backstop, not the only defence. Patterns anchor on the capability vocabulary, which is a closed set. |
| The gate over-triggers and corrects an accurate confirmation | A claim matching a recorded success passes byte-identical; the recorded outcome is the authority, not the pattern. Covered by an explicit test and a spec edge case. |
| Sentence buffering introduces visible stutter | Only claim-bearing sentences are held, and only to their boundary. TTS already segments by sentence, so this is the existing granularity. |
| Full-width bubbles look wrong for long prose | Known and accepted (FR-017a); verify at step 5 of the US3 quickstart. |
| Frontend suite flakiness masks a real regression | Known issue — `ChatPage.test.tsx` under parallel load. Re-run failures in isolation before treating them as this feature's. |

## Complexity Tracking

No constitutional violations. Table intentionally empty.
