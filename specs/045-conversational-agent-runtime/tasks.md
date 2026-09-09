---

description: "Task list for 045-conversational-agent-runtime"
---

# Tasks: Conversational Agent Runtime

**Input**: Design documents from `/specs/045-conversational-agent-runtime/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md) (21 decisions), [data-model.md](./data-model.md), [contracts/](./contracts/) (5), [quickstart.md](./quickstart.md)

**Tests**: **Included and mandatory.** Constitution §10 requires tests for new behaviour in the same PR that introduces it — this is not the optional case.

**Organization**: Grouped by user story. US1–US3 are P1 and form the MVP; US4–US6 are P2; US7–US8 are P3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US8, mapping to spec.md user stories

## Path Conventions

Clean Architecture backend plus a co-located React SPA:

- `src/AskLucy.Domain/`, `src/AskLucy.Application/`, `src/AskLucy.Infrastructure/`, `src/AskLucy.Persistence/`, `src/AskLucy.Web/`
- Frontend: `src/AskLucy.Web/ClientApp/src/`
- Tests: `tests/AskLucy.Application.Tests/`, `tests/AskLucy.Web.Tests/`, `tests/AskLucy.Persistence.Tests/`, and `*.test.tsx` beside components

---

## Phase 1: Setup

**Purpose**: Folders, options and registration scaffolding. No behaviour.

- [X] T001 [P] Create the `Conversations` namespace folders in `src/AskLucy.Application/Conversations/` — `Runtime/`, `Capabilities/`, `Flows/`, `Prompts/`, `SystemAgents/`
- [X] T002 [P] Create `ConversationRuntimeOptions` in `src/AskLucy.Application/Options/ConversationRuntimeOptions.cs` with `MaxCapabilityInvocationsPerTurn` (3), `MaxDelegationsPerTurn` (3), `MaxTurnDurationSeconds`, `MaxSuggestedActions` (5), `IndexRetrievalThreshold` (10), `IndexRetrievalTopN` (8)
- [X] T003 [P] Bind `ConversationRuntimeOptions` from configuration in `src/AskLucy.Application/DependencyInjection.cs` and add defaults to `src/AskLucy.Web/appsettings.json`
- [X] T004 [P] Add `TurnOrchestration` to the `AiCapability` enum in `src/AskLucy.Domain/Ai/AiCapability.cs`, appended after `BoundaryVision` so no persisted integer shifts (data-model.md §6)
- [X] T005 Create the test folder `tests/AskLucy.Application.Tests/Conversations/` with `Capabilities/`, `Flows/`, `Runtime/` subfolders

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The substrate every user story stands on — the handler extraction, the schema, the capability abstraction and the decide step.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### 2a. Handler extraction (do this FIRST — plan.md risk)

`SendChatMessageCommandHandler` is a 356-line method mixing six concerns. It must become a thin delegation *before* new beats are added, or this feature adds a second orchestration layer on top of the first instead of replacing it.

- [X] T006 Characterisation tests for today's `SendChatMessageCommandHandler` behaviour in `tests/AskLucy.Application.Tests/Ai/SendChatMessageCommandHandlerCharacterisationTests.cs` — chunk order, `__LOCATION__`-before-boundary, boundary isolation, budget exhaustion paths. These must pass before and after extraction.
- [X] T007 Create `IConversationTurnOrchestrator` in `src/AskLucy.Application/Conversations/Runtime/IConversationTurnOrchestrator.cs` returning `IAsyncEnumerable<ChatStreamChunk>`
- [X] T008 Create `ConversationTurnOrchestrator` in `src/AskLucy.Application/Conversations/Runtime/ConversationTurnOrchestrator.cs`, moving the existing RAG → memory → location → zoom → boundary body across **unchanged in behaviour**
- [X] T009 Reduce `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs` to validation, provider/model resolution and delegation to the orchestrator; register the orchestrator in DI
- [X] T010 Verify T006's characterisation tests still pass unchanged — the extraction must be behaviour-neutral

### 2b. Domain and persistence (data-model.md)

- [X] T011 [P] Create `SuggestedAction` value object in `src/AskLucy.Domain/Conversations/SuggestedAction.cs` — `Kind`, `Key`, `Text`, `Label`, `Description`, `ArgumentsJson`, `IsDecline` (data-model.md §1)
- [X] T012 [P] Add `SuggestedActionsJson`, `SelectedActionKind`, `SelectedActionKey`, `SelectedActionArgumentsJson` (all nullable) to `src/AskLucy.Domain/Chats/Message.cs`, set at creation only — the aggregate stays append-only
- [X] T013 [P] Add `SystemKey`, `IsSystemOwned`, `ModelCapability` to `src/AskLucy.Domain/Agents/Agent.cs`
- [X] T014 [P] Make `ModelProviderId`/`ModelId` nullable and add `DefinitionHash` to `src/AskLucy.Domain/Agents/AgentVersion.cs`; keep `Agent.Publish` requiring both for user agents
- [X] T015 [P] Create `UserConversationPreference` in `src/AskLucy.Domain/Chats/UserConversationPreference.cs`, mirroring `UserPanelPreference`
- [X] T016 Update EF configurations in `src/AskLucy.Persistence/Configurations/` for Message, Agent, AgentVersion; add the unique filtered index `IX_Agents_SystemKey` and the `UserConversationPreferences` table with a unique index on `UserId`
- [X] T017 Generate the `AddConversationalAgentRuntime` migration in `src/AskLucy.Persistence/Migrations/`; check the generated file for a BOM and `System.*` usings ordered first before commit (repo CI convention)
- [ ] T018 [P] Persistence tests for the new columns and the filtered index in `tests/AskLucy.Persistence.Tests/ConversationalAgentRuntimeSchemaTests.cs`

### 2c. Capability abstraction (contracts/conversation-capability.md)

- [X] T019 Create `TurnContext` in `src/AskLucy.Application/Conversations/Capabilities/TurnContext.cs` — active location, active boundary, attached KB ids, documents flag, memory availability, open panel keys, granted permissions, subscription tier (data-model.md §8)
- [X] T020 Create `IConversationCapability : IAgentTool` in `src/AskLucy.Application/Conversations/Capabilities/IConversationCapability.cs` with `WhenToUse`, `ArgumentHint`, `UsageGuidance`, `Label`, `OfferDescription`, `AcknowledgementTemplate`, `IsAvailable`, `IsOfferable` (defaulting to `IsAvailable`), `ExpectedDuration`
- [X] T021 Create `CapabilityIndexEntry` and `ConversationCapabilityCatalog` in `src/AskLucy.Application/Conversations/Capabilities/ConversationCapabilityCatalog.cs` — `BuildIndexAsync`, `AvailableFor`, `OfferableFor`, `Find`; reads `AgentToolCatalog.All` live. **The catalog — not each capability — enforces entitlement** (FR-011 rule 3): a capability whose `RequiredPermissions` the user lacks, or whose subscription tier they are not on, is filtered out before any capability's own `IsAvailable` is consulted, so a new capability cannot forget the check.
- [X] T022 Create `CapabilityIndexRetriever` in `src/AskLucy.Application/Conversations/Capabilities/CapabilityIndexRetriever.cs` — embeds the user message via `IEmbeddingService`, returns top-N Tier 1 entries **plus every context-gated available capability**; falls back to the full index with one logged warning when the model file is absent (research.md D14)
- [X] T023 [P] `resolve_location` capability in `src/AskLucy.Application/Conversations/Capabilities/ResolveLocationCapability.cs`, wrapping the existing geocoding path
- [X] T024 [P] `resolve_site_boundary` capability in `src/AskLucy.Application/Conversations/Capabilities/ResolveSiteBoundaryCapability.cs`, wrapping `IBoundaryResolutionService`; `ExpectedDuration = Long`
- [X] T025 [P] `adjust_viewer_focus` capability in `src/AskLucy.Application/Conversations/Capabilities/AdjustViewerFocusCapability.cs`
- [X] T026 [P] `search_knowledge_base` capability in `src/AskLucy.Application/Conversations/Capabilities/SearchKnowledgeBaseCapability.cs`, wrapping `IRagService`
- [X] T027 [P] `search_memory` capability in `src/AskLucy.Application/Conversations/Capabilities/SearchMemoryCapability.cs`, wrapping `IMemoryService`
- [X] T028 [P] `open_visual_panel` capability in `src/AskLucy.Application/Conversations/Capabilities/OpenVisualPanelCapability.cs`, reusing the specs/028 panel-type registry
- [X] T029 Make `McpToolAdapter` implement `IConversationCapability` in `src/AskLucy.Application/Mcp/Tools/McpToolAdapter.cs`, deriving `Label` from the server-declared title and availability from server-active plus permission checks
- [X] T030 Register all capabilities in `src/AskLucy.Application/DependencyInjection.cs`
- [X] T031 Shared theory test over every registered `IConversationCapability` in `tests/AskLucy.Application.Tests/Conversations/Capabilities/CapabilityContractTests.cs` — index entries non-empty, within length limits, third person, ≥2 trigger terms in `WhenToUse`; `ExecuteAsync` failure returns `AgentToolResult.Failure` and never throws
- [X] T032 [P] Per-capability availability and offerability tests in `tests/AskLucy.Application.Tests/Conversations/Capabilities/` — each precondition false independently, non-redundancy, and the five capabilities that are never offerable. Include catalog-level entitlement tests: a capability is absent from the index when the user lacks a `RequiredPermission` or the subscription tier, even though its own `IsAvailable` returns true (FR-011).

### 2d. The decide step (contracts/turn-stream.md §1)

> **Resequenced during implementation (2026-09-08).** T033/T034/T039 — the prompt, the parser and
> their tests — are additive and are done. **T035–T038 moved to Phase 3 (US1).** They remove the
> location intent classifier, `ViewerZoomDetector` and the confirmation templates, but the decide
> step only *replaces* those once the orchestrator consumes its verdict and emits beats, which is
> US1's work. Deleting them here would leave location resolution broken between phases for no
> benefit, and wiring the decide call in without consuming it would just buy a second model call
> per turn that changes nothing. The task list assumed they could land together; they cannot.

> **Completed 2026-09-08, with four scope calls made explicit rather than silently narrowed:**
>
> 1. **`LocationResolutionService.ResolveAsync` (classify+resolve+back-reference) was kept, not
>    deleted.** A new `ResolveQueryAsync(userChatId, query, ct)` was added instead — geocode,
>    score and validate only, no classification — and `ResolveLocationCapability` calls that. The
>    double-classification race T036 exists to remove is gone from the live turn path. The old
>    classifier method is now unused by that path but not deleted: doing so would have required
>    deleting or redesigning ~600 lines of its own passing test coverage (ambiguity, back-reference,
>    timeout) with no replacement design for back-reference detection at the decide-step level.
>    Recorded as a deliberate, narrower scope than T036's literal wording, not an oversight.
> 2. **`ViewerZoomDetector`/`IViewerZoomDetector` were deleted outright**, along with their DI
>    registration and their own test file — a pure keyword matcher with no external dependency and
>    no equivalent design gap, unlike the location classifier above.
> 3. **`ConversationTurnOrchestrator`'s act path runs slices sequentially, in decision order, with
>    no automatic dependency-result-passing.** Genuine multi-slice wiring is Phase 7 (sub-agent
>    delegation). A single-capability turn — what US1's acceptance criteria describe — is
>    unaffected. One acknowledgement covers the whole turn rather than one per slice, since a
>    compound request naming several capabilities is not yet a designed scenario.
> 4. **`TurnIntent.Suggest` takes the fast path** (an ordinary reply, no beats, no offer) until
>    Phase 4 builds the offer step. **Entitlement is a placeholder**: every authenticated user is
>    granted the three low-risk permissions the built-in capabilities declare, since no granular
>    per-user permission system surfaces to chat today — a real source is a genuine gap, not an
>    oversight, and is called out in `ConversationTurnOrchestrator.BuildTurnContext`.
>
> Three retired-behaviour test files were deleted alongside the rewrite —
> `SendChatMessageLocationIntegrationTests.cs`, `SendChatMessageBoundaryIntegrationTests.cs`, and
> `SendChatMessageCommandHandlerCharacterisationTests.cs` (whose job, guarding T007–T010's
> behaviour-neutral extraction, was complete and already committed). Their coverage is superseded
> by `TurnDecisionParserTests`, `CapabilityContractTests`, `CapabilityAvailabilityTests`,
> `CapabilityExecutorTests` and the new `ConversationTurnOrchestratorBeatTests`.


- [X] T033 [P] Create `TurnDecisionPrompt` (v1) in `src/AskLucy.Application/Conversations/Prompts/TurnDecisionPrompt.cs` as a versioned artifact — carries the Tier 1 index only, never input schemas; defines `intent` as `answer` | `act` | `suggest` with the borderline-resolves-to-`suggest` rule
- [X] T034 Create `TurnDecision` and `TurnDecisionParser` in `src/AskLucy.Application/Conversations/Runtime/` — JSON with one corrective retry, reusing `AgentPlanner`'s idiom; logs unparseable content with a bounded prefix, provider and model
- [X] T035 Wire the decide step into `ConversationTurnOrchestrator`, resolving its provider/model through `AiCapabilityProviderResolver.ResolveAsync(AiCapability.TurnOrchestration)`
- [X] T036 Retire the location intent classifier: remove `LocationIntentClassificationPromptV1` and its model call from `src/AskLucy.Application/Locations/LocationResolutionService.cs`, keeping geocoding, confidence scoring and outcome shaping (research.md D11)
- [X] T037 Delete `src/AskLucy.Application/Locations/ViewerZoomDetector.cs` and its registration; zoom is now `adjust_viewer_focus` (FR-047)
- [X] T038 Reduce `LocationConfirmationTemplates` to FR-008 fallback wording only, no longer the normal user-facing prose
- [X] T039 [P] Decide-step tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/TurnDecisionParserTests.cs` — valid JSON, markdown-fenced JSON, unparseable after retry, unrecognised intent, all three intents

### 2e. Guards, budget and the turn record

- [ ] T040 Wire `AgentBudgetGuard`, `AgentDuplicateToolCallDetector` and `AgentPolicyEvaluator` into `ConversationTurnOrchestrator` — reused, not reimplemented (research.md D1)
- [ ] T041 Create `TurnRecorder` in `src/AskLucy.Application/Conversations/Runtime/TurnRecorder.cs` writing `AgentExecution` / `AgentExecutionStep` / `AgentToolCall` / `AgentExecutionError` rows for capability-invoking turns; fast-path turns create no row (research.md D8)
- [ ] T042 Add the capability-invocation isolation wrapper to the orchestrator, following the `ResolveBoundarySafelyAsync` pattern — linked cancellation token (not `Task.WaitAsync`), original-token re-adjudication so user cancellation is never recorded as a timeout

**Checkpoint**: capabilities resolve, the decide step returns a verdict, the turn record is written, and the existing chat behaviour is unchanged.

---

## Phase 3: User Story 1 — Lucy Works the Request Out Loud (P1) 🎯 MVP

**Goal**: A request that needs an action produces an acknowledgement before the work, a result written from the real outcome, and continuous named progress throughout.

**Independent test**: Send "show me &lt;place&gt;" and confirm, as separate messages and in order, an acknowledgement naming the work (delivered before it starts) then a result message written from the actual outcome — with no gap over 5 s without a named progress indication.

- [X] T043 [US1] Implement the acknowledge beat in `ConversationTurnOrchestrator` using the selected capability's `AcknowledgementTemplate` — templated, never model-generated, emitted the instant routing resolves (research.md D15)
- [X] T044 [US1] Implement the act-and-report beat: run the capability, then narrate the **real** outcome via `TurnNarrationPrompt`
- [X] T045 [P] [US1] Create `TurnNarrationPrompt` (v1) in `src/AskLucy.Application/Conversations/Prompts/TurnNarrationPrompt.cs` — receives the capability's Tier 2 `UsageGuidance` and the actual result; must reflect success, partial success or failure accurately (FR-007)
- [X] T046 [US1] Emit each beat as `StartsNewMessage: true` with a `PendingLabel` naming the work, reusing the specs/044 mechanism (research.md D5)
- [X] T047 [US1] Implement the fast path (FR-006): `intent: "answer"` streams a plain reply with no acknowledgement beat, no offer step and no `AgentExecution` row
- [X] T048 [US1] Implement FR-008 fallback wording for every outcome type when narration fails, so a turn is never left without a user-visible statement
- [X] T049 [US1] Add the SSE keep-alive comment (every 10 s while a beat is pending) in `src/AskLucy.Web/Controllers/v1/AiController.cs` (research.md D5)
- [X] T050 [US1] Preserve the specs/044 ordering guarantees in the new orchestrator: `__LOCATION__` written and flushed the moment its chunk is yielded, before any long step
- [X] T051 [P] [US1] Frontend: render `pendingLabel` as a named progress indication in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`, replaced by content when it arrives — **already satisfied**: `useChatStream.ts`/`ChatPage.tsx` built this generically for specs/044 (a `messageBreak` opens an empty bubble immediately, rendered as `ThinkingIndicator` with the pending label until content arrives). The new orchestrator's beats reuse the identical wire shape, so no frontend code changed; T053 adds the test proving it.
- [X] T052 [P] [US1] Orchestrator beat tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/ConversationTurnOrchestratorBeatTests.cs` — acknowledgement precedes work, result written from the real outcome, failure never narrated as success, fast path emits no beats
- [X] T053 [P] [US1] Frontend tests for progress rendering in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.test.tsx`, and update `ChatPage.test.tsx` (it carries its own message-rendering assertions)

**Checkpoint**: US1 is independently demonstrable — a single-capability turn narrates correctly end to end.

---

## Phase 4: User Story 2 — Lucy Offers Only What She Can Actually Do (P1)

**Goal**: When Lucy judges an offer useful, she presents a bounded, grounded, mixed list — and otherwise presents nothing at all.

**Independent test**: Complete an action in two different conversation states and confirm the offered sets differ, that every action row maps to a registered available capability or flow variant, and that a fast-path turn produces no offer and no offer model call.

> **Phase 4 scope decisions.** (1) The offer step and grounder support all four row kinds structurally, but `flowVariant` rows are always ungrounded for now — no flow registry exists until Phase 6 (T081-T093), so the prompt only offers `capability`/`followUp` and the grounder discards any `flowVariant` a model names anyway, logged the same as a hallucinated key. (2) FR-025a.3 (`UserDeclinedLastOffer`) and the "previously offered" half of FR-025a.4 are wired into `TurnOutcome` and `OfferSuppressionRules` but always evaluate to their safe defaults (never declined, nothing previously offered) — reading the real values back needs a persisted offer to have existed and been acted on, which is exactly what Phase 5's selection dispatch (T068+) adds; same documented-placeholder pattern as `BuildTurnContext`'s FR-011 rule 3 entitlement check. (3) `Message.SuggestedActionsJson` persists the whole `SuggestedActionOffer` (question + rows), not the bare `SuggestedAction[]` data-model.md §1 describes — the question is itself composed per turn and SC-009 requires a reopened conversation to reproduce it exactly, and no separate column exists for it; the envelope is a strict superset of the array, so no migration was needed. (4) FR-032's per-user toggle always evaluates to enabled — the real preference read/write is T121 (Phase 11).

- [X] T054 [P] [US2] Create `SuggestedActionPrompt` (v1) in `src/AskLucy.Application/Conversations/Prompts/SuggestedActionPrompt.cs` — receives the capability index (FR-024a), relevant memories and the turn outcome; may legitimately return no actions (FR-025c)
- [X] T055 [US2] Implement the offer step in `ConversationTurnOrchestrator`, invoked only when no suppression rule applies
- [X] T056 [US2] Implement the five suppression rules (FR-025a) — fast-path turn, nothing offerable, user just declined, same options ignored last turn, feature disabled — each skipping the step entirely with no model call
- [X] T057 [US2] Create `SuggestedActionGrounder` in `src/AskLucy.Application/Conversations/Runtime/SuggestedActionGrounder.cs` implementing FR-024 per kind: absolute registry + schema check for `flowVariant`/`capability`; best-effort doing-phrasing check for `followUp`; every discard logged with the proposed key or text and the reason
- [X] T058 [US2] Append the decline row server-side (FR-022); enforce the `MaxSuggestedActions` cap; drop the whole offer rather than showing a partial one that fails validation
- [X] T059 [US2] Feed relevant memories into the offer step so follow-ups draw on comparable situations (FR-021b.1)
- [X] T060 [US2] Add `SuggestedActions` to `ChatStreamChunk` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`
- [X] T061 [US2] Emit the `__ACTIONS__` SSE event in `src/AskLucy.Web/Controllers/v1/AiController.cs`, written **after** the offering assistant message is persisted so `offeredByMessageId` is real
- [X] T062 [US2] Persist `SuggestedActionsJson` on the offering assistant message (FR-026)
- [X] T063 [P] [US2] Add the `actions` variant to `ChatStreamEvent` and `suggestedActions`/`question` to `ChatMessage` in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts`; parse `__ACTIONS__` in `streamChat`
- [X] T064 [P] [US2] Carry actions through `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts` onto the message
- [X] T065 [P] [US2] Grounder tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/SuggestedActionGrounderTests.cs` — unregistered capability key discarded and logged; arguments failing the schema discarded; a `followUp` promising platform work discarded; a valid mixed offer passes intact
- [X] T066 [P] [US2] Suppression tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/OfferSuppressionTests.cs` — one test per FR-025a rule asserting **no model call is made**
- [X] T067 [P] [US2] `aiApi` parsing tests for `__ACTIONS__` in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts`

**Checkpoint**: offers appear when useful, are grounded per kind, and are absent otherwise.

---

## Phase 5: User Story 3 — Choosing an Option Does Exactly That (P1)

**Goal**: Selecting a row runs precisely that, against the context Lucy already established.

**Depends on**: US2 (there must be an offer to select).

**Independent test**: Complete an action, select an offered row, and confirm the bound work runs with no re-resolution and no clarifying question; then select a stale offer and confirm a visible refusal.

> **Phase 5 backend/frontend split.** T068-T074 and T080 (the dispatch pipeline itself — resolution, grounding, the three Problem Details types, orchestrator dispatch, persistence) shipped first and are independently testable end to end via the API (a client can already select an offer by posting `selectedAction` on `/api/v1/ai/chat`). T075-T079 (the `SuggestedActionCard` UI, its wiring into `MessageBubble`/`ChatPage`, and its two test files) are the follow-on commit — there is no user-facing way to select an offer yet, only the machinery that runs a selection correctly once one arrives. `RunOfferStepAsync` was also folded into a new shared `EmitOfferIfDueAsync` (used by both the decide-based path and selection dispatch) as part of this work, and `ConversationTurnOrchestrator`'s old private `BuildTurnContext` moved out to a reusable `Conversations/Capabilities/TurnContextFactory` so `SelectedActionResolver` can build the identical snapshot to re-check availability at dispatch time.

- [X] T068 [US3] Add `SelectedAction` (`OfferedByMessageId`, `Kind`, `Key`, `Text`, `Arguments`) to `SendChatMessageCommand` and the Web request model
- [X] T069 [US3] Extend `SendChatMessageCommandValidator` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandValidator.cs` — non-empty `OfferedByMessageId`, recognised `Kind`, `Arguments` a JSON object
- [X] T070 [US3] Implement staleness and ownership checks: the referenced message must be in this chat, assistant-role, carry this row, and be the **newest unanswered** offer → else `409 conversation-action-stale` (FR-029)
- [X] T071 [US3] Re-evaluate availability at dispatch against a freshly built `TurnContext` for `flowVariant`/`capability` → else `409 conversation-action-unavailable` (FR-028); a `followUp` has nothing to re-check
- [X] T072 [US3] Implement dispatch per kind (FR-027): `capability` runs it with the decide step skipped; `followUp` runs **no capability** and answers from the echoed text; `decline` performs no work and suppresses the next turn's offer
- [X] T073 [US3] Persist the selection on the user message created by it (`SelectedActionKind`/`Key`/`ArgumentsJson`), with the row's label as `Content` (research.md D6)
- [X] T074 [US3] Add the three Problem Details types (`conversation-action-stale`, `conversation-action-unavailable`, `conversation-action-unknown`) to the Web layer's problem mapping
- [ ] T075 [P] [US3] Build `SuggestedActionCard` in `src/AskLucy.Web/ClientApp/src/features/chat/components/SuggestedActionCard.tsx` — topic chip, question, radio rows with label + description, decline last, submit disabled until selected, inline error Alert, inert mode for history. **No free-text row.**
- [ ] T076 [US3] Render the card from `MessageBubble` only for the newest unanswered offer; earlier offers render inert (FR-029)
- [ ] T077 [US3] Wire selection dispatch in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` with an explicit `catch` reaching a visible error — no un-awaited promise anywhere in this path (constitution §2.VIII)
- [ ] T078 [P] [US3] Card component tests in `src/AskLucy.Web/ClientApp/src/features/chat/components/SuggestedActionCard.test.tsx` — live, submitting, error and inert states; composer stays enabled throughout
- [ ] T079 [P] [US3] Card a11y tests in `src/AskLucy.Web/ClientApp/src/features/chat/components/SuggestedActionCard.a11y.test.tsx` — `role="radiogroup"`, label as accessible name, description via `aria-describedby`, `aria-busy`, `role="alert"` (constitution §10 requires automated a11y for novel patterns)
- [X] T080 [P] [US3] Dispatch tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/SelectedActionDispatchTests.cs` — exact dispatch, no re-resolution, stale refusal, unavailable refusal, decline path, that a `followUp` invokes zero capabilities (SC-002a), and that an ordinary typed message dismisses a pending offer without error and is handled as a normal turn (FR-030)

**🎯 MVP COMPLETE** — US1 + US2 + US3 deliver the agentic turn end to end.

---

## Phase 6: User Story 4 — Lucy Runs a Multi-Step Job End to End (P2)

**Goal**: `locate_a_place` runs as one narrated job for a navigational request, and is offered by variant for an informational one.

**Independent test**: "Show me &lt;place&gt;" runs all three steps in order with the N+1 narration cadence; "Do you know &lt;place&gt;?" runs nothing and offers the variants.

- [ ] T081 [P] [US4] Create `IConversationFlow`, `FlowVariant`, `FlowStep` (with `AnnouncementTemplate`, `CompletionTemplate`, `BindArguments`, `IsAlreadySatisfied`, `IsRequired`) and `FlowStepContext` in `src/AskLucy.Application/Conversations/Flows/`
- [ ] T082 [US4] Create `LocateAPlaceFlow` in `src/AskLucy.Application/Conversations/Flows/LocateAPlaceFlow.cs` — three steps per contracts/capability-flow.md, with the `focus` and `full` variants
- [ ] T083 [US4] Create `ConversationFlowCatalog` in `src/AskLucy.Application/Conversations/Flows/ConversationFlowCatalog.cs`; include flows in the Tier 1 index alongside capabilities
- [ ] T084 [US4] Implement `FlowRunner` in `src/AskLucy.Application/Conversations/Flows/FlowRunner.cs` — dependency-ordered execution, result passed forward (FR-055), stop-on-failure (FR-056), skip-when-satisfied (FR-057)
- [ ] T085 [US4] Implement the uniform N+1 narration cadence (FR-052/FR-053): first message announces step 1; each subsequent message pairs the completed step's `CompletionTemplate` with the next step's `AnnouncementTemplate`; the last reports alone
- [ ] T086 [US4] Implement stop-and-name-the-cause reporting (FR-056, research.md D19) — the user-facing text names **only** what failed; unattempted steps and reasons go to the record
- [ ] T087 [US4] Implement intent gating (FR-051a): `act` runs the flow, `suggest` answers then offers the variants, passing mention does neither
- [ ] T088 [US4] Implement variant dispatch (FR-051c) — an accepted variant runs from step 1 with identical narration
- [ ] T089 [US4] Implement flow scoping and interruption (FR-058) — "just find it" runs a prefix; interruption stops at the current step and records the flow interrupted
- [ ] T090 [US4] Remove automatic boundary resolution from the orchestrator; it now exists only as step 3 of the flow (FR-046)
- [ ] T091 [US4] Record one `AgentExecutionStep` per flow step including skipped and unattempted ones with reasons (FR-061)
- [ ] T092 [P] [US4] Flow tests in `tests/AskLucy.Application.Tests/Conversations/Flows/LocateAPlaceFlowTests.cs` — happy path with N+1 messages; failure at each of the three step positions; already-satisfied skip; scoped request; interruption; budget stop; and that no flow step appears as an independent offer (FR-060)
- [ ] T093 [P] [US4] Intent-gating tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/FlowIntentGatingTests.cs` — navigational runs and does not offer; informational offers and does not move the viewer; passing mention does neither; borderline resolves to informational

---

## Phase 7: User Story 5 — Lucy Delegates to Sub-Agents (P2)

**Goal**: A request spanning two areas is split across sub-agents and answered in one turn.

**Independent test**: One message requiring a site lookup and a knowledge search returns both, narrated as they happen, with the site result passed forward rather than re-derived.

- [ ] T094 [US5] Create `SubAgentDelegator` in `src/AskLucy.Application/Conversations/Runtime/SubAgentDelegator.cs` — runs independent slices concurrently, dependent slices in order, passing results forward (FR-017)
- [ ] T095 [US5] Give each concurrent slice its own `IServiceScope` — never the request's scoped `DbContext` (research.md D12; this repo has shipped that bug before)
- [ ] T096 [US5] Enforce `MaxDelegationsPerTurn` and duplicate-delegation detection via the reused guards (FR-019)
- [ ] T097 [US5] Implement partial-failure reporting (FR-018) — successful slices delivered, failed one named
- [ ] T098 [US5] Scope each sub-agent's capability set so it can resolve only its own area's capabilities (FR-016)
- [ ] T099 [P] [US5] Delegation tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/SubAgentDelegatorTests.cs` — two-area request, dependency ordering, result forwarding, partial failure, delegation cap, and capability scoping (the knowledge sub-agent resolves no viewer capability)
- [ ] T100 [P] [US5] Concurrency test asserting each parallel slice resolves its own scope and no `DbContext` is shared

---

## Phase 8: User Story 6 — Lucy Is a Real, Inspectable Agent (P2)

**Goal**: The agent catalog is no longer empty, and every turn is inspectable.

**Independent test**: On a fresh database the five system agents exist after startup; restarting publishes no new versions; a mutation attempt returns 403; a completed turn's record shows decisions, delegations and discarded suggestions.

- [ ] T101 [P] [US6] Create `SystemAgentDefinition` and `SystemAgentDefinitions` in `src/AskLucy.Application/Conversations/SystemAgents/` — the five definitions with `ComputeHash()` (contracts/system-agent-provisioning.md)
- [ ] T102 [P] [US6] Create `ISystemAgentProvisioner` and `SystemAgentProvisioningResult` in `src/AskLucy.Application/Conversations/SystemAgents/`
- [ ] T103 [US6] Implement `SystemAgentProvisioner` in `src/AskLucy.Infrastructure/Conversations/SystemAgentProvisioner.cs` — upsert by `SystemKey`, publish a new version only on hash change, null model binding, uniqueness-violation recovery
- [ ] T104 [US6] Add `SystemAgentProvisioningHostedService` following the `ProviderHealthCheckHostedService` pattern; a database that is unreachable or has pending migrations logs a warning and returns `Deferred` without throwing
- [ ] T105 [US6] Surface a `Deferred` provisioning result as degraded on `/health/ready`
- [ ] T106 [US6] Resolve a null-model `AgentVersion` at run time through `AiCapabilityProviderResolver` using the agent's `ModelCapability`
- [ ] T107 [US6] Add a shared system-agent mutation guard and call it from every agent mutation command in `src/AskLucy.Application/Agents/Commands/`, returning `403 system-agent-immutable`
- [ ] T108 [P] [US6] Expose `isSystemOwned` on agent DTOs and badge system agents read-only in the agents UI under `src/AskLucy.Web/ClientApp/src/features/agents/`
- [ ] T109 [P] [US6] Provisioner tests in `tests/AskLucy.Application.Tests/Conversations/SystemAgents/SystemAgentProvisionerTests.cs` — fresh database creates five; second run writes nothing; changed definition publishes exactly one new version preserving history; pending migrations defer; concurrent provisioners do not duplicate
- [ ] T110 [P] [US6] Theory test over every agent mutation command asserting `403` for a system agent and unchanged behaviour for a user agent
- [ ] T111 [P] [US6] Turn-record tests asserting decisions, delegations, discarded suggestions and cost are all retrievable (FR-038)

---

## Phase 9: User Story 7 — The Turn Works by Voice (P3)

**Goal**: Beats are spoken as they arrive; option labels are spoken, descriptions and payloads are not.

**Independent test**: Run an action turn with voice enabled and confirm per-beat speech, labels spoken, descriptions silent, persona unchanged across two languages.

- [ ] T112 [US7] Speak each beat as it arrives rather than at turn end, in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`
- [ ] T113 [US7] Build the spoken offer string from question + non-decline labels only; never pass descriptions, keys or arguments to `useVoiceOutput` (FR-044)
- [ ] T114 [P] [US7] Voice tests asserting per-beat speech, label-only offer speech, that no structured payload reaches the speech path, and that the configured voice persona is unchanged across at least two supported languages — FR-045 mandates no change, so the test guards against regression rather than driving new code

---

## Phase 10: User Story 8 — Nothing Fails Silently, and History Tells the Truth (P3)

**Goal**: Every failure reaches the user in the same turn; a reopened conversation replays exactly what was displayed.

**Independent test**: Force a failure at each stage and confirm visible feedback every time; reload and confirm the transcript matches.

- [ ] T115 [US8] Implement decision-step degradation (FR-039) — plain reply plus a visible explanation; execution `Completed` with a `TerminationReason`, not `Failed`
- [ ] T116 [US8] Implement turn-budget stop (FR-009) — partial results kept plus an explicit "I stopped there"
- [ ] T117 [US8] Implement interrupted-turn recording (FR-042), distinguishable from completed and failed
- [ ] T118 [US8] Render offers and selections from persisted history so a reopened conversation replays them (FR-026, SC-009)
- [ ] T119 [P] [US8] Failure-matrix tests in `tests/AskLucy.Application.Tests/Conversations/Runtime/TurnFailureMatrixTests.cs` — one test per row of contracts/turn-stream.md §7, each asserting user-visible text
- [ ] T120 [P] [US8] Replay test asserting a reloaded conversation reproduces beats, offers and selections in order

---

## Phase 11: Polish & Cross-Cutting

- [ ] T121 [P] Add the conversation preference endpoints (`GET`/`PUT /api/v1/chats/preferences`) with command, query, validator and repository, mirroring the Panels preference pattern
- [ ] T122 [P] Add the suggested-actions toggle to `src/AskLucy.Web/ClientApp/src/features/settings/pages/ChatConfigurationTab.tsx`; with it off, historical offers render as plain text
- [ ] T123 Update `ReplyScopePromptFraming` in `src/AskLucy.Application/Ai/ReplyScopePromptFraming.cs` — keep the anti-essay rules, replace "confirm and stop" with turn-taking guidance (spec reconciliation, 010)
- [ ] T124 [P] Verify the pre-feature conversation compatibility path (FR-049) with a test opening a conversation created before this migration
- [ ] T125 [P] Deploy `App_Data/embedding-models/{model.onnx,vocab.txt}` and document the step in `docs/` — required for capability-index retrieval, and it also activates the dormant local RAG embedding path
- [ ] T126 Run the full backend suite (`dotnet test "Ask Lucy.sln"`) and the **full** frontend suite (`npx tsc -b --noEmit` then `npm test` — not `tsc --noEmit`, which checks nothing in this repo)
- [ ] T127 Walk every scenario in [quickstart.md](./quickstart.md), including the six regression checks
- [ ] T128 [P] Update `docs/CONVERSATIONAL_AGENT_RUNTIME.md` status from "design" to "implemented" and mark `docs/LOCATION_TO_BOUNDARY_END_TO_END.md` as superseded
- [ ] T129 [P] Add an ADR in `docs/adr/` recording the two reversals on the record: boundary opt-in → flow step, and registered follow-ups → composed follow-ups with a split grounding guarantee

### Constitution-mandated gates (added by `/speckit-analyze`)

Constitution §16 blocks merge on tests (§10), documentation (§13) and a performance review (§15). These tasks exist because the first pass omitted them.

- [ ] T130 **Capture the pre-feature baseline** before Phase 2 changes behaviour: first-token latency (p50/p90) and model calls per turn for an action turn and a no-action turn, recorded in `specs/045-conversational-agent-runtime/baseline.md`. Every performance and cost criterion is relative to this, so it cannot be measured after the fact.
- [ ] T131 [P] Performance tests in `tests/AskLucy.Web.Tests/Performance/ConversationTurnPerformanceTests.cs` asserting the plan's stated goals — first visible output p90 ≤ 2.5 s, decide step p90 ≤ 1.2 s, no gap > 5 s without a named indication (SC-001, SC-004a). Gate behind the repo's existing `RUN_SCALE_PERFORMANCE_TESTS` environment flag, matching how the other scale tests are held until go-live. **Constitution §10 requires these for any path with a stated performance goal.**
- [ ] T132 [P] End-to-end test in `tests/AskLucy.E2E.Tests/` covering the full narrated flow through the real API and UI — "show me &lt;place&gt;" producing four messages, the viewer moving, and no option card. §10 names "first chat" as a critical journey and this is now that journey.
- [ ] T133 [P] Emit per-turn metrics (token count, latency, capability-invocation count, discarded-suggestion count) alongside the `AgentExecution` record, so SC-008's cost budget is dashboardable rather than log-only (constitution §14)
- [ ] T134 [P] Build the benchmark utterance set in `tests/AskLucy.Application.Tests/Conversations/Benchmarks/` — navigational, informational, passing-mention and multi-area requests — and the harness that measures SC-002b (≤2% follow-ups promising platform work), SC-003 (≥95% selections need no clarification) and SC-005 (≥90% multi-part answered in one turn). These three criteria are unverifiable without it.
- [ ] T135 [P] Update `docs/DATABASE.md` and `docs/ENTITY_MODEL.md` for the four schema changes, and the OpenAPI document for the two new preference endpoints and the `selectedAction` request member (constitution §13, §16.3)
- [ ] T136 Complete the §16 gate checklist in the PR description — architecture, testing, documentation, accessibility, performance and security reviews, with an explicit one-line justification for any marked not-applicable

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)**: no dependencies
- **Foundational (2)**: depends on Setup — **blocks every user story**. Within it, **2a must complete first**: extracting the handler before adding beats is what keeps this a replacement rather than a second layer.
- **US1 (3)**: depends on Foundational only
- **US2 (4)**: depends on Foundational; independent of US1 in principle, but demonstrating it needs a turn that did work, so US1 first is the practical order
- **US3 (5)**: **depends on US2** — there must be an offer to select. This is a genuine cross-story dependency, not an artefact.
- **US4 (6)**: depends on Foundational; its variant-offering half depends on US2/US3
- **US5 (7)**: depends on Foundational
- **US6 (8)**: depends on Foundational; T111's record assertions are strongest after US4/US5 exist
- **US7 (9)**: depends on US1 (beats to speak) and US2 (offers to read)
- **US8 (10)**: depends on all preceding stories, since it tests their failure paths
- **Polish (11)**: after the desired stories

### Parallel opportunities

- Setup: T001–T004 all `[P]`
- Foundational 2b: T011–T015 `[P]` (different files); T016–T017 serialise on the migration
- Foundational 2c: the six capabilities T023–T028 are `[P]`
- US1: T045, T051, T052, T053 `[P]`
- US2: T063, T064, T065, T066, T067 `[P]`
- US3: T075, T078, T079, T080 `[P]`
- US4: T092, T093 `[P]`; US5: T099, T100 `[P]`; US6: T101, T102, T108–T111 `[P]`
- Different stories can be staffed in parallel once Foundational completes, except US3 which waits on US2

### Parallel example — Foundational capabilities

```bash
Task: "resolve_location capability in src/AskLucy.Application/Conversations/Capabilities/ResolveLocationCapability.cs"
Task: "resolve_site_boundary capability in .../ResolveSiteBoundaryCapability.cs"
Task: "adjust_viewer_focus capability in .../AdjustViewerFocusCapability.cs"
Task: "search_knowledge_base capability in .../SearchKnowledgeBaseCapability.cs"
Task: "search_memory capability in .../SearchMemoryCapability.cs"
Task: "open_visual_panel capability in .../OpenVisualPanelCapability.cs"
```

---

## Implementation Strategy

### MVP — US1 + US2 + US3

1. Phase 1 Setup
2. Phase 2 Foundational — **2a first**, and confirm T010's characterisation tests pass before proceeding
3. Phase 3 US1 → validate the three-beat turn independently
4. Phase 4 US2 → validate grounded, suppressible offers
5. Phase 5 US3 → validate selection dispatch
6. **STOP and validate**: quickstart scenarios 1, 2, 3 and 8

At this point Lucy is genuinely agentic for single-capability turns, with grounded offers the user can act on. That is a shippable increment.

### Incremental delivery after MVP

- **US4** adds the multi-step job — the largest single UX change, and the one that retires automatic boundary resolution
- **US5** adds real multi-agent delegation
- **US6** fills the empty agent catalog and makes turns inspectable
- **US7/US8** complete voice and the failure/replay guarantees

### Notes

- Every capability, flow and follow-up path must satisfy constitution §2.VIII — no swallowed exception, no un-awaited promise, no console-only error
- Prompts (T033, T045, T054) are versioned artifacts; a revision is a `V2` constant, never an edit in place (constitution §9)
- Run the **full** frontend suite, not the touched file — `ChatPage.test.tsx` carries assertions no component test will catch
- `PERSISTENCE_TESTS_CONNECTION_STRING` must be set or nearly every `Web.Tests` test fails at Hangfire startup
