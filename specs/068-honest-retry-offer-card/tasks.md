---

description: "Task list for 068-honest-retry-offer-card"
---

# Tasks: Honest Turn Outcomes, Real Retry, and a Readable Offer Card

**Input**: Design documents from `/specs/068-honest-retry-offer-card/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included. Not optional for this feature — constitution §10 (Testing Standards) and §19 (Definition of Done) require unit and integration coverage, and SC-001's "guarantee of the system rather than a best-effort target" is only demonstrable by test.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 — maps to the user stories in spec.md
- Exact file paths are given in every task

## Path Conventions

Clean Architecture backend under `src/AskLucy.*`, React SPA under `src/AskLucy.Web/ClientApp/src`, tests under `tests/AskLucy.*.Tests` and co-located `*.test.tsx`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the suites runnable and capture a pre-change baseline, so a later failure is attributable.

- [ ] T001 Set `PERSISTENCE_TESTS_CONNECTION_STRING` from `src/AskLucy.Web/appsettings.Development.json` (shared site4now test DB, not LocalDB — LocalDB cannot run the full-text migrations) and confirm `dotnet test tests/AskLucy.Web.Tests` reaches Hangfire startup
- [ ] T002 [P] Record a baseline run of `cd src/AskLucy.Web/ClientApp && npm test` and note which `ChatPage.test.tsx` cases are already flaky under parallel load, so Phase 5 failures are attributable
- [X] T003 [P] Confirm `cd src/AskLucy.Web/ClientApp && npx tsc -b --noEmit` passes (a bare `tsc --noEmit` silently checks nothing in this repo)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Record every turn's real outcome and make it retrievable. This is the single authority that US1 verifies against and US2 replays from.

**⚠️ CRITICAL**: US1 and US2 cannot begin until this phase is complete. **US3 does not depend on this phase at all** and may run concurrently or ship first.

### Domain and model

- [X] T004 [P] Create `TurnVerdict` enum (`AnsweredInWords`, `Acted`, `FailedBeforeCompleting`) in `src/AskLucy.Application/Conversations/Runtime/TurnVerdict.cs`
- [X] T005 [P] Create `ActionAttempt` record (Kind, Key, TargetLabel, ArgumentsJson, Succeeded, FailureReason) in `src/AskLucy.Application/Conversations/Runtime/ActionAttempt.cs`
- [X] T006 Create `RecordedTurnOutcome` record (Verdict, Attempts, FailureReason, RecordedAtUtc) with the validation rules from data-model.md §2 in `src/AskLucy.Application/Conversations/Runtime/RecordedTurnOutcome.cs` — `AnsweredInWords` ⇒ empty Attempts; `Acted` ⇒ non-empty; `Succeeded == false` ⇒ FailureReason required
- [X] T007 [P] Unit-test `RecordedTurnOutcome` validation rules in `tests/AskLucy.Application.Tests/Conversations/Runtime/RecordedTurnOutcomeTests.cs`

### Persistence

- [X] T008 Add nullable `TurnOutcomeJson` string property to `src/AskLucy.Domain/Chats/Message.cs`, mirroring `SuggestedActionsJson`
- [X] T009 Configure the column as `nvarchar(max)` nullable in the message entity configuration under `src/AskLucy.Persistence/`
- [X] T010 Generate the additive migration (`dotnet ef migrations add AddMessageRecordedTurnOutcome`) — nullable, no backfill; verify no BOM and no `\r\r\n` line endings before committing
- [X] T011 Extend `AppendMessageCommand` (and its handler) to accept and persist the serialized outcome in the same transaction as the message, so FR-004d has no partial state to detect
- [X] T012 [P] Persistence test for `TurnOutcomeJson` round-trip including a null outcome on a legacy message, in `tests/AskLucy.Persistence.Tests/` — guard with `PERSISTENCE_TESTS_DEDICATED_DATABASE=1`

### Recording on both paths

- [ ] T013 Build a `RecordedTurnOutcome` on the normal completion path in `src/AskLucy.Application/Conversations/Runtime/ConversationTurnOrchestrator.cs`, populating `Attempts` from each capability's own reported result (FR-001)
- [ ] T014 Build a `FailedBeforeCompleting` outcome in the mid-stream catch at `src/AskLucy.Web/Controllers/v1/AiController.cs:265` and pass it to `PersistAssistantMessageAsync` **before** the failure notice is written (FR-004c) — this is the exact path that recorded nothing in production
- [ ] T015 Thread the outcome through `PersistAssistantMessageAsync` in `src/AskLucy.Web/Controllers/v1/AiController.cs` into `AppendMessageCommand`
- [ ] T016 Surface a failed outcome persist rather than continuing silently (FR-004d) in `src/AskLucy.Web/Controllers/v1/AiController.cs`
- [ ] T017 Make `TurnRecorder.RecordAsync` reachable from the mid-stream failure path and extend it to record answered-in-words turns, in `src/AskLucy.Application/Conversations/Runtime/TurnRecorder.cs` and `ConversationTurnOrchestrator.cs:248` (FR-004e) — keep its existing swallow-and-log behaviour intact (FR-004f)
- [ ] T018 [P] Unit-test that a mid-stream failure still produces a persisted outcome and an audit record, in `tests/AskLucy.Application.Tests/Conversations/Runtime/ConversationTurnOrchestratorTests.cs`
- [ ] T019 [P] Unit-test that an audit-trail write failure does not fail or delay the turn (FR-004f), in `tests/AskLucy.Application.Tests/Conversations/Runtime/TurnRecorderTests.cs` — note `Received().Log(...)` never matches `[LoggerMessage]` source-gen logging; assert on behaviour, not the logger
- [ ] T020 [P] Assert the audit trail is never *read* as the outcome source (FR-004b's exclusivity): grep-level or architecture test that reply composition, the claim gate and retry resolution all read `Message.TurnOutcomeJson` and none reference `AgentExecution`, in `tests/AskLucy.Application.Tests/Conversations/Runtime/TurnOutcomeAuthorityTests.cs`

### Transport

- [ ] T021 Emit the `__TURN_OUTCOME__` sentinel event before `[DONE]` on every path in `src/AskLucy.Web/Controllers/v1/AiController.cs`, using the payload in contracts/turn-outcome.md §1 — **omit `argumentsJson`**
- [ ] T022 Return `turnOutcome` on assistant messages from the transcript endpoint using the same payload shape, so the client parses one shape (SC-001c)
- [ ] T023 [P] Add the `__TURN_OUTCOME__` sentinel and a `turnOutcome` field on `ChatMessage` in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`
- [ ] T024 [P] Integration-test that every turn type emits exactly one `__TURN_OUTCOME__` and that `argumentsJson` never appears in the stream, in `tests/AskLucy.Web.Tests/`

**Checkpoint**: Every turn now leaves a retrievable, message-attached outcome. US1 and US2 can begin.

---

## Phase 3: User Story 1 - Lucy never claims an action she did not perform (Priority: P1) 🎯 MVP

**Goal**: No reply ever asserts a workspace action succeeded unless that action reported success.

**Independent test**: Force an action to fail, continue the conversation several turns, and confirm no reply claims the action completed — per [quickstart.md](./quickstart.md) §US1.

### Tests first

- [ ] T025 [P] [US1] Unit-test the claim gate's behaviour table from contracts/turn-outcome.md §3 in `tests/AskLucy.Application.Tests/Conversations/Runtime/TurnOutcomeClaimGateTests.cs`: claim-free sentence released immediately; claim + matching success released **byte-identical**; claim + recorded failure withheld and replaced; claim + no matching attempt withheld; claim + unavailable outcome withheld (FR-002c)
- [ ] T026 [P] [US1] Unit-test that a replacement is never empty, blank or a truncated sentence (FR-002b), in the same file
- [ ] T027 [P] [US1] Unit-test that buffering never exceeds one sentence for claim-free text (SC-001b), in the same file

### Implementation

- [ ] T028 [US1] Implement sentence-boundary segmentation in `src/AskLucy.Application/Conversations/Runtime/TurnOutcomeClaimGate.cs`, matching the granularity `src/AskLucy.Application/Ai/TextToSpeechStreamer.cs` already uses
- [ ] T029 [US1] Generate action-claim patterns from the closed vocabulary in `src/AskLucy.Application/Conversations/Capabilities/ConversationCapabilityCatalog.cs` (13 capability types) rather than hand-writing them, in `src/AskLucy.Application/Conversations/Runtime/TurnOutcomeClaimGate.cs` — hand-written patterns are the feature's main residual risk
- [ ] T030 [US1] Implement match-against-outcome and withhold-and-replace, composing the replacement from the recorded `FailureReason` (FR-002a, FR-002b)
- [ ] T031 [US1] Implement the FR-002c fallback: with no retrievable outcome, suppress action-claim sentences rather than release them unverified
- [ ] T032 [US1] Wire the gate into the single content-delta write site at `src/AskLucy.Web/Controllers/v1/AiController.cs:184`, leaving the other sentinel writes untouched
- [ ] T033 [US1] Ensure the persisted assistant content is the **gated** text, not the raw stream, so a reload cannot resurrect a withheld claim
- [ ] T034 [US1] Add the recent-outcome summary to the fast path's system framing in `src/AskLucy.Application/Ai/ReplyScopePromptFraming.cs`, stating which recent actions did not succeed (Layer 1 — removes the model's motive to fabricate)
- [ ] T035 [US1] Mark the failure-notice message as a failure in the history passed to the composer (FR-007) in `src/AskLucy.Application/Conversations/Runtime/ConversationTurnOrchestrator.cs` `RunFastPathAsync` — annotate or reframe any message whose `TurnOutcomeJson` is `FailedBeforeCompleting` so it cannot read as Lucy's substantive answer. Without this the notice prose still arrives looking like a normal reply, which is step 8 of the causal chain in research.md
- [ ] T036 [US1] Report partial success per attempt rather than as one verdict (FR-006) wherever the outcome is rendered into prose

### Verification

- [ ] T037 [P] [US1] Integration-test the full reproduction: fail the router's provider, restore it, send "try again", assert no success claim appears — `tests/AskLucy.Web.Tests/`
- [ ] T038 [P] [US1] Integration-test that a genuinely successful confirmation passes through unchanged (false-positive guard, the spec's "verification disagrees with a correct reply" edge case)

**Checkpoint**: The reported harm is stopped. US1 is independently shippable without US2 or US3.

---

## Phase 4: User Story 2 - "Try again" actually tries again (Priority: P2)

**Goal**: A retry re-executes the original action with the original parameters and reports the fresh outcome.

**Independent test**: Fail a workspace action, ask to retry in ordinary language, confirm re-execution — per [quickstart.md](./quickstart.md) §US2.

### Routing summary

- [ ] T039 [P] [US2] Unit-test `RecentTurnOutcomeSummary`: capped at 3 turns, constant size as the conversation grows, projected from outcomes not prose, empty when there are no outcomes (FR-009a–c, SC-009) — `tests/AskLucy.Application.Tests/Conversations/Runtime/RecentTurnOutcomeSummaryTests.cs`
- [ ] T040 [US2] Implement `src/AskLucy.Application/Conversations/Runtime/RecentTurnOutcomeSummary.cs` per data-model.md §3
- [ ] T041 [US2] Accept the summary as a third input in `src/AskLucy.Application/Conversations/Runtime/TurnDecider.cs` and render it in `TurnDecisionPrompt.Build(...)` using the format in contracts/turn-outcome.md §4
- [ ] T042 [US2] Assert the prompt is byte-identical to today's when the summary is empty (FR-009c), in `tests/AskLucy.Application.Tests/Conversations/Runtime/TurnDeciderTests.cs`
- [ ] T043 [US2] Fix the supported retry-phrasing set as a named test fixture and assert routing accuracy against it (SC-003, SC-010 both specify >=95% but neither is measurable today), in `tests/AskLucy.Application.Tests/Conversations/Runtime/RetryPhrasingRoutingTests.cs` — cover at minimum "try again", "retry that", "do it again", "can you try once more", plus a negative case that must ask rather than guess

### Retry resolution

- [ ] T044 [P] [US2] Unit-test `RetryTargetResolver` resolution rules from data-model.md §4 in `tests/AskLucy.Application.Tests/Conversations/Runtime/RetryTargetResolverTests.cs`: no outcome → unknown (400); no failed attempt → unknown (400); already succeeded → refusal (FR-015); target no longer resolves → stale (409)
- [ ] T045 [P] [US2] Security-test that a `failedMessageId` from another user's chat resolves as **not found**, never as a permission error confirming it exists
- [ ] T046 [US2] Implement `src/AskLucy.Application/Conversations/Runtime/RetryTargetResolver.cs` mirroring `SelectedActionResolver`, reusing `ConversationActionUnknownException` / `ConversationActionStaleException` / `ConversationActionUnavailableException`
- [ ] T047 [US2] Add `RetryRequest(Guid FailedMessageId)` and the optional `Retry` member to `ChatRequest` in `src/AskLucy.Web/Contracts/AiContracts.cs`
- [ ] T048 [US2] Reject a request carrying both `SelectedAction` and `Retry` as a 400, in `src/AskLucy.Web/Controllers/v1/AiController.cs`
- [ ] T049 [US2] Dispatch a resolved retry straight into the act path, bypassing `TurnDecider`, in `src/AskLucy.Web/Controllers/v1/AiController.cs` — read every parameter server-side from the persisted outcome, never from the request (FR-010)

### Transcript behaviour

- [ ] T050 [US2] Append a new assistant turn with its own outcome and leave the failed turn intact (FR-013a); ensure **no** user message is inserted, diverging deliberately from selected-action dispatch (FR-013b)
- [ ] T051 [US2] Make the retry's outcome statement distinguishable from the original attempt's (FR-011, SC-005) — not a verbatim repeat of the first failure notice
- [ ] T052 [US2] Ask which action to retry when a typed retry is ambiguous, and name the action retried when defaulting to the most recent failure (FR-012)
- [ ] T053 [P] [US2] Integration-test the transcript shape after a retry: two assistant turns, both with retrievable outcomes after a reload, no synthetic user message (SC-012) — `tests/AskLucy.Web.Tests/`

### Client affordance

- [ ] T054 [US2] Add a retry control to the existing specs/046 action row in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`, shown only when `turnOutcome` has a failed attempt
- [ ] T055 [US2] Send `retry: { failedMessageId }` from `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` — message id only, never capability or arguments
- [ ] T056 [US2] Give the activation handler an explicit awaited error path surfacing failures as visible UI feedback, never a console log (FR-014, and the project's error-handling rules)
- [ ] T057 [P] [US2] Component-test the retry affordance's visibility rules, busy state and error surfacing in `MessageBubble.test.tsx`; register the endpoint in the MSW handlers so an unmocked request cannot reach the real network

**Checkpoint**: Recovery from a failed action works by control and by typed language.

---

## Phase 5: User Story 3 - The offer card is readable, and not read aloud in full (Priority: P3)

**Goal**: The offer card uses the panel's width, and voice speaks a cue rather than reciting the card.

**Independent test**: Trigger a long offer in docked and expanded views — per [quickstart.md](./quickstart.md) §US3.

**Note**: Independent of Phases 2–4. Can be implemented, tested and shipped on its own.

### Width

- [ ] T058 [US3] Make the bubble's `maxWidth` conditional on offer presence in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx:97` — full usable panel width when the message carries an offer, unchanged 75% otherwise (FR-016, FR-017)
- [ ] T059 [US3] Remove the card's own `maxWidth: '75%'` in `src/AskLucy.Web/ClientApp/src/features/chat/components/SuggestedActionCard.tsx:93` (FR-016a) — both caps must go, since 75% of 75% ≈ 56% is the reported symptom
- [ ] T060 [P] [US3] Keep reply prose readable at full width in an offer-carrying bubble (FR-017a, the accepted trade-off)

### Internal layout

- [ ] T061 [US3] Put each option's control, label and description on a shared horizontal band in `SuggestedActionCard.tsx` (FR-018)
- [ ] T062 [US3] Keep the confirm action fully visible with its label unwrapped at every supported width (FR-019)
- [ ] T063 [US3] Degrade to a stacked layout at the narrowest supported panel width -- `min(92vw, 380px)` per `ExpandedChatPanel.tsx:77`, i.e. ~294px on a 320px viewport -- with no clipping or horizontal scrolling (FR-021, SC-007); wrap an over-long single label **within** the card
- [ ] T064 [US3] Apply the same width behaviour to answered and historical cards without making them interactive (FR-020)

### Voice scope

- [ ] T065 [US3] Replace the question-plus-every-label enumeration at `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx:435-465` with a short localized cue (FR-023, FR-024), appended to the reply being spoken rather than interrupting it
- [ ] T066 [US3] Add the cue string to the localization resources for every supported language — the young-adult female voice persona is unaffected, this governs only what is spoken

### Verification

- [ ] T067 [P] [US3] Component-test width behaviour in `SuggestedActionCard.test.tsx`: full width with an offer, ordinary width without, both in one transcript (SC-006a); assert via `getByText` inside the card, since `getByRole` crashes jsdom once a dialog portal is open
- [ ] T068 [P] [US3] Component-test that voice receives the reply plus the cue and **not** the question, labels, descriptions or confirm action (SC-011), in `ChatPage.test.tsx`
- [ ] T069 [P] [US3] Accessibility-test the card at both rendered widths with no regression against the baseline from T002 (FR-022, SC-008)
- [ ] T070 [US3] Screenshot-verify the quickstart §US3 steps at the default docked width (400px, the `sm` value in `ExpandedChatPanel.tsx:77`), counting words per line on option labels and the Choose button against the 4-word threshold (SC-006)

**Checkpoint**: All three stories complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T071 [P] Run the **full** frontend suite, not just touched files — `ChatPage.test.tsx` carries its own assertions about components it renders; re-run any failure in isolation before attributing it to this feature
- [ ] T072 [P] Run `dotnet format` and verify CI line-ending rules before pushing
- [ ] T073 [P] Confirm `npx tsc -b --noEmit` still passes
- [ ] T074 Update architecture and API documentation for the `__TURN_OUTCOME__` event and the retry request (constitution §13 — documentation is part of the implementation)
- [ ] T075 Add migration notes for `AddMessageRecordedTurnOutcome` (additive, nullable, no backfill)
- [ ] T076 Run the full quickstart against a real host boot, not just unit tests — a required-options or DI-cycle regression is invisible to `dotnet build`
- [ ] T077 Verify on production after deploy: reproduce the original Al Safa Park 2 sequence and confirm no false success claim

---

## Dependencies

```
Phase 1 (Setup)
    │
    ├──> Phase 2 (Foundational: turn outcome recorded + retrievable)
    │         │
    │         ├──> Phase 3 (US1 — claim gate)          🎯 MVP
    │         │         │
    │         │         └──> Phase 4 (US2 — retry)
    │         │
    │         └──────────────┘
    │
    └──> Phase 5 (US3 — card + voice)   ← independent of Phase 2-4
                                          │
Phase 6 (Polish) <────────────────────────┘
```

**Story dependencies**:

- **US1** requires Phase 2 (it verifies against the recorded outcome).
- **US2** requires Phase 2 and benefits from US1 but does not strictly require it — the routing summary and retry resolver stand alone. Sequenced after US1 because US1 stops the reported harm.
- **US3** requires nothing from the others.

**Within Phase 2**: T004–T006 → T007; T008 → T009 → T010; T011 → T013–T015; T013/T014 → T021.

**Blocking detail**: T014 (outcome on the mid-stream failure path) is the single most important task in the feature — it is the exact gap that produced the production defect.

## Parallel Execution Examples

**Phase 2 setup** — different files, no interdependencies:

```
T004 (TurnVerdict.cs)  ║  T005 (ActionAttempt.cs)
```

**Phase 3 tests** — all in one new test file but independent cases; write together:

```
T025  ║  T026  ║  T027
```

**Phase 4** — routing and retry are separate subsystems:

```
T039 (summary tests)   ║  T044 (resolver tests)  ║  T045 (security test)
```

**Phase 5** — all three verification tasks touch different test files:

```
T067 (card width)  ║  T068 (voice scope)  ║  T069 (a11y)
```

**Cross-story** — once Phase 1 is done, a second developer can take Phase 5 end-to-end in parallel with Phases 2–4.

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** That stops the reported harm: Lucy can no longer claim an action she did not perform. It ships without retry and without the card fix.

**Increment 2 = Phase 4 (US2).** Restores the recovery path so a user need not retype their request.

**Increment 3 = Phase 5 (US3).** Independent and low-risk; a reasonable candidate to ship **first** as a separate PR if the backend work needs longer review, since it is the visible half of the user's report.

**Sequencing rationale**: the user reported the styling defect and the false claim together, but they are unrelated in code. Coupling them in one PR would hold a two-file CSS fix behind a migration and a streaming change.
