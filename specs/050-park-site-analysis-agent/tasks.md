# Tasks: Conversational Park Site Analysis Agent

**Input**: Design documents from `/specs/050-park-site-analysis-agent/`

**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included throughout, per this repo's constitution §10 ("tests are written for new behavior in the same PR that introduces it") and every prior feature's tasks.md in this repository — not because the spec explicitly requested TDD, but because it is this codebase's established convention.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2/P2/P3/P3) to enable independent implementation and testing of each story. Two repositories are involved: `hydra` (this repo, C#/TypeScript) and the sibling `park-redesign` (Python MCP server) — every task states which repo it belongs to.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US6, matching spec.md)
- Paths without a repo prefix are in `hydra`; paths under `park-redesign/` are in the sibling repo

---

## Phase 1: Setup

**Purpose**: Record the two new-dependency ADRs this plan's Complexity Tracking flags, and scaffold the new Python package.

- [X] T001 [P] Author `docs/adr/0008-park-redesign-mcp-server-dependency.md` per plan.md's Complexity Tracking row 1 and research.md Decision 4 (context, decision, alternatives considered, consequences)
- [X] T002 [P] Author `docs/adr/0009-thedigitalcore-service-account-integration.md` per plan.md's Complexity Tracking row 2 and research.md Decision 3
- [X] T003 [P] Scaffold the new Python package in `park-redesign/mcp_server/` (`pyproject.toml` with the official `mcp` SDK dependency, package `__init__.py`, `tests/` folder, `pytest` config) per plan.md's Project Structure
- [X] T004 [P] Add `TheDigitalCoreIntegration` configuration skeleton (base URL, service-account credential placeholder — secret-free, per ADR-0001's convention) to `src/AskLucy.Web/appsettings.json` and the gitignored `appsettings.Development.json.example`

**Checkpoint**: ADRs recorded, Python package importable, configuration skeleton in place.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The one new database entity, the TheDigitalCore client, and a minimally-working MCP server (boundary resolution only) that every user story needs before any of them can run.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Create `SiteAnalysisProjectLink` entity + `SiteAnalysisProjectLinkSource` enum (`InboundDeepLink`/`BootstrapCreated`/`BootstrapMatched`) in `src/AskLucy.Domain/SiteAnalysis/SiteAnalysisProjectLink.cs` per data-model.md
- [X] T006 Create `SiteAnalysisProjectLinkConfiguration` (EF Core Fluent API: `BaseEntity` conventions, unique index on `UserChatId`, index on `TheDigitalCoreProjectId`) in `src/AskLucy.Persistence/Configurations/SiteAnalysis/SiteAnalysisProjectLinkConfiguration.cs` (depends on T005)
- [X] T007 Add `SiteAnalysisProjectLink` `DbSet` to `AskLucyDbContext` and generate the `AddSiteAnalysisProjectLink` EF Core migration in `src/AskLucy.Persistence/Migrations/` (depends on T006)
- [X] T008 [P] Define `ISiteAnalysisProjectLinkRepository` in `src/AskLucy.Application/Abstractions/ISiteAnalysisProjectLinkRepository.cs` (depends on T005)
- [X] T009 Implement `SiteAnalysisProjectLinkRepository` in `src/AskLucy.Persistence/Repositories/SiteAnalysisProjectLinkRepository.cs` (depends on T008, T007)
- [X] T010 [P] Define `ITheDigitalCoreClient` (`FindProjectAsync`, `CreateProjectAsync`, `RelayCategoryScoreResultAsync`) in `src/AskLucy.Application/Abstractions/ITheDigitalCoreClient.cs` per contracts/thedigitalcore-integration-api.md
- [X] T011 Implement `TheDigitalCoreIntegrationOptions` (`IOptions<T>`, `ValidateOnStart`, service-account credential bound from configuration/secret manager, never logged) in `src/AskLucy.Infrastructure/TheDigitalCore/TheDigitalCoreIntegrationOptions.cs` (depends on T004, T010)
- [X] T012 Implement `TheDigitalCoreClient` (named `IHttpClientFactory` client, service-account bearer auth, name-then-geolocation search per research.md Decision 8, create, relay) in `src/AskLucy.Infrastructure/TheDigitalCore/TheDigitalCoreClient.cs` (depends on T010, T011)
- [X] T013 Register `TheDigitalCoreClient`, its named `HttpClient`, and `TheDigitalCoreIntegrationOptions` in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T012)
- [X] T014 [P] Implement the `resolve_site_boundary` MCP tool (wraps notebook Module 01 verbatim; success = existence + boundary per research.md Decision 5; explicit `resolved: false` / ambiguous-candidate output) in `park-redesign/mcp_server/tools/resolve_site_boundary.py` per contracts/site-analysis-mcp-tools.md (depends on T003)
- [X] T015 Implement `park-redesign/mcp_server/server.py` (official `mcp` SDK, Streamable HTTP transport, registers `resolve_site_boundary`) (depends on T014)
- [ ] T016 [P] Add dev-seed data in `src/AskLucy.Web/DevSeed/` for: an empty "Site Analysis Knowledge Base", registration of the new MCP server endpoint (configuring its upstream mapping/imagery/vision-provider credentials — FR-009 — through the existing MCP credential-storage mechanism) with `resolve_site_boundary` activated, and the one Site Analysis Agent (Type=Task, OutputFormat=Json) in Draft with `resolve_site_boundary` + the built-in Knowledge Search tool attached, then Published as AgentVersion 1 (depends on T013, T015)
- [ ] T016a Ingest real methodology/standards/case-study source content (from `AI-Assisted_Urban_Park_Analysis_Framework.docx` and its cited sources) into the Site Analysis Knowledge Base via the existing RAG ingestion pipeline (FR-019/FR-020), so `score_recreation`/`score_social`'s `citationRef` values (contracts/site-analysis-mcp-tools.md) resolve to real, retrievable passages rather than an empty knowledge base (depends on T016)

**Deferred (not yet attempted)**: T016/T016a require a running instance of the new Python MCP server (T015) to actually complete capability discovery against, and require deciding the real Site Analysis Agent/Knowledge Base creation flow's exact command sequence against a live database — neither is available in this environment. T001-T015 are implemented and build-verified (`dotnet build` on the full solution: 0 errors).

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Verify a real site and create its digital project on request (Priority: P1) 🎯 MVP

**Goal**: A user describes a real park in chat; the assistant resolves its location and built-asset status, searches TheDigitalCore, and — only on explicit confirmation — creates or links a Project (FR-001a-FR-001g).

**Independent Test**: Open a new conversation with no Project linked, describe a real site with no matching TheDigitalCore Project, confirm the existence-check + "not found" + creation-offer sequence, confirm, and verify a new Project + `SiteAnalysisProjectLink` (`BootstrapCreated`) appear.

### Tests for User Story 1

- [X] T017 [P] [US1] Unit tests for `SiteAnalysisChatTurnRouter`'s bootstrap-trigger recognition (site description with no link yet; a mid-bootstrap confirmation reply) in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisChatTurnRouterBootstrapTests.cs`
- [X] T018 [P] [US1] Unit tests for `VerifyAndLinkDigitalCoreProjectCommandHandler` (location-unresolvable-blocks path FR-001b, resolved-but-not-built-asset-proceeds-as-planned path FR-001g, no-match-offers-create path FR-001e, match-links-existing path FR-001d, ambiguous-candidates-ask-user path) in `tests/AskLucy.Application.Tests/SiteAnalysis/VerifyAndLinkDigitalCoreProjectCommandHandlerTests.cs`
- [X] T019 [P] [US1] Integration tests for `TheDigitalCoreClient` against a stubbed `HttpMessageHandler` (search-by-name, search-by-location fallback, create) in `tests/AskLucy.Infrastructure.Tests/SiteAnalysis/TheDigitalCoreClientTests.cs`
- [X] T020 [P] [US1] Domain tests for `SiteAnalysisProjectLink` invariants (unique per `UserChatId`, identifying fields immutable after creation, soft delete only) in `tests/AskLucy.Domain.Tests/SiteAnalysis/SiteAnalysisProjectLinkTests.cs`
- [ ] T021 [P] [US1] Playwright E2E for the full bootstrap flow (describe site → location resolution → not-found → offer → confirm → Project created) in `tests/AskLucy.E2E.Tests/SiteAnalysisBootstrap.spec.ts` — **not runnable in this environment** (no running frontend/backend + authenticated session available), same documented constraint as other specs in this repo (e.g. specs/014's Playwright note)

### Implementation for User Story 1

- [X] T022 [US1] Implement `VerifyAndLinkDigitalCoreProjectCommand` + handler (calls `resolve_site_boundary` for location resolution + built-asset status, then `ITheDigitalCoreClient.FindProjectAsync`, then creates/links only after explicit confirmation) in `src/AskLucy.Application/SiteAnalysis/Commands/VerifyAndLinkDigitalCoreProject/` (depends on T009, T012, T013, T016)
- [X] T023 [US1] Implement `VerifyAndLinkDigitalCoreProjectCommandValidator` (FluentValidation: never proceed to TheDigitalCore search when the location itself is unresolvable — FR-001b; a resolved-but-not-built-asset site proceeds normally — FR-001g; never create without explicit confirmation) in `src/AskLucy.Application/SiteAnalysis/Commands/VerifyAndLinkDigitalCoreProject/VerifyAndLinkDigitalCoreProjectCommandValidator.cs` (depends on T022)
- [X] T024 [US1] Implement `SiteAnalysisChatTurnRouter`'s bootstrap-trigger recognition (site description before any link; confirmation reply) and its call into `StartAgentExecutionCommand` per contracts/chat-to-agent-routing.md in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T022)
- [X] T025 [US1] Wire `SiteAnalysisChatTurnRouter` into the existing `AppendMessageCommand` pipeline via a new `IPipelineBehavior<AppendMessageCommand, MessageDto>` (`SiteAnalysisChatTurnBehavior`) — corrected from "SendMessageCommandHandler", which does not exist in this codebase; the real chat-append entry point is `AppendMessageCommandHandler` — in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnBehavior.cs` (depends on T024)
- [X] T026 [US1] Implement the bootstrap-completion reaction as `SiteAnalysisCompletionReactionJob` (`ISiteAnalysisCompletionReactionJob`), scheduled via `Hangfire.BackgroundJob.ContinueJobWith` off the Hangfire job id `IAgentExecutionRunner.EnqueueAsync` now returns (an additive signature change, `Task` → `Task<string>`, verified against all 3 existing callers + 18 existing Agent tests, all still passing) — corrected from an assumed `AgentExecutionCompleted` MediatR notification, which does not exist in this codebase; completion is handled entirely inline in `AgentExecutionOrchestrator.RunAsync` with no observable hook, discovered during implementation and resolved as documented in `docs/adr/0009` and `ISiteAnalysisCompletionReactionJob`'s own doc comment — in `src/AskLucy.Application/SiteAnalysis/AgentExecutionCompletion/SiteAnalysisCompletionReactionJob.cs` (depends on T022)
- [X] T027 (superseded) — no domain events are raised. Research assumed an existing "domain event dispatched post-commit" convention; none exists in this codebase (discovered during implementation, same finding `Project.cs` already documented for spec 018) — `SiteAnalysisProjectLink`'s own doc comment records this. `VerifyAndLinkDigitalCoreProjectCommandHandler`/`SiteAnalysisCompletionReactionJob` call the repository directly within the same transaction instead.

**Also discovered during implementation**: `AgentExecution.FinalOutputJson` is hardcoded by the orchestrator to `{ citations: [...] }`, not a free-form shape driven by `Agent.OutputFormat` as research.md Decision 6 assumed. `SiteAnalysisCompletionReactionJob`/`SiteAnalysisChatTurnRouter` instead read the specific tool's `AgentExecutionStep.OutputJson` directly (already real, per-step structured data — no new table needed, Decision 6's actual spirit is preserved even though its literal mechanism wasn't available).

**Checkpoint**: User Story 1 is fully functional and testable independently.

---

## Phase 4: User Story 2 - Describe a site in chat and see its boundary appear in the Immersive Viewer (Priority: P1)

**Goal**: Once a Project is linked (US1, or via deep link), naming a site or giving coordinates renders a boundary in the Immersive Viewer, reused for later turns rather than re-resolved.

**Independent Test**: Type a place name or lat/lon into a site-linked conversation and confirm a boundary outline/marker appears in the viewer; ask a follow-up and confirm no re-resolution occurs. Separately, follow a Project-linked deep link from TheDigitalCore and confirm it lands in an already-linked conversation.

### Tests for User Story 2

- [X] T028 [P] [US2] Unit tests for `SiteAnalysisChatTurnRouter`'s post-link site/coordinate recognition and boundary-reuse (no new `resolve_site_boundary` call for an unrelated follow-up) — covered by `HandleUserMessage_ShouldNoOp_WhenAlreadyLinkedAndBoundaryAlreadyResolved` in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisChatTurnRouterBootstrapTests.cs` (a dedicated `...BoundaryTests.cs` file was not needed — the router's post-link branch is small enough to cover alongside the existing bootstrap tests)
- [X] T029 [P] [US2] Unit tests for `SiteAnalysisCompletionReactionJob`'s boundary-render branch (dispatches `IPanelNotifier.PanelRequestedAsync` with `typeKey = "site-analysis-boundary"`) in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisCompletionReactionJobTests.cs` — renamed from the originally-planned `SiteAnalysisAgentExecutionCompletionHandler...` (the actual completion mechanism is a Hangfire-continuation job, `SiteAnalysisCompletionReactionJob`, not a handler — see the note on T036 below)
- [X] T030 [P] [US2] Unit tests for the ambiguous-resolution clarifying-question path (`candidateCount > 1`) — `ProcessAsync_ShouldAskForClarification_WithoutDispatchingAPanelOrSearching_WhenCandidatesAreAmbiguous` in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisCompletionReactionJobTests.cs`
- [X] T031 [P] [US2] Integration test for `CreateSiteAnalysisProjectLinkFromDeepLinkCommandHandler` (valid project reference links/opens a conversation; invalid reference surfaces a Problem Details error) in `tests/AskLucy.Application.Tests/SiteAnalysis/CreateSiteAnalysisProjectLinkFromDeepLinkCommandHandlerTests.cs`
- [ ] T032 [P] [US2] Playwright E2E for boundary rendering and reuse in `tests/AskLucy.E2E.Tests/SiteAnalysisBoundary.spec.ts` — not runnable in this environment (no browser/E2E harness available); deferred, documented limitation matching T021's precedent
- [ ] T033 [P] [US2] Playwright E2E for the deep-link entry path (FR-024a) in `tests/AskLucy.E2E.Tests/SiteAnalysisDeepLinkEntry.spec.ts` — not runnable in this environment; same deferral as T032

### Implementation for User Story 2

- [X] T034 [US2] Implement `SiteAnalysisConversationStateAssembler` in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisConversationStateAssembler.cs` (depends on T024). Scoped narrower than originally planned: it currently assembles only the last resolved `resolve_site_boundary` step (`LastResolvedBoundaryStep`) shared between the router and the completion job — per-category completion state (US3/US4) will extend this record when those stories are implemented, rather than being speculatively built now (YAGNI).
- [X] T035 [US2] Extend `SiteAnalysisChatTurnRouter` to recognize a plain site-name/coordinate message once a `SiteAnalysisProjectLink` already exists, using `SiteAnalysisConversationStateAssembler` to avoid re-resolving an already-known boundary (FR-005) in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T034)
- [X] T036 [US2] Implement the boundary-render branch (dispatch a `site-analysis-boundary` panel via the existing `IPanelNotifier`/`PanelHub` mechanism) in `src/AskLucy.Application/SiteAnalysis/AgentExecutionCompletion/SiteAnalysisCompletionReactionJob.cs` (depends on T026, T035). **Research.md Decision 11 correction, discovered during implementation**: the Immersive Viewer's command API (`ClientApp/src/viewer/api/commands.ts`, `ViewerEngine.ts`) is a purely client-side TypeScript surface with no backend→frontend push channel of its own — there is no "dispatch a viewer command" mechanism to call into from the backend. Reused the already-existing `IPanelNotifier`/`PanelHub` SignalR push (spec 028) instead, with a new panel type (`site-analysis-boundary`, `ClientApp/src/viewer/panels/types/site-analysis-boundary/SiteAnalysisBoundaryPanel.tsx`) whose renderer forwards the payload into the real `addLayer`/`zoomToLocation` viewer commands as a mount-time side effect, then displays as a normal dismissible confirmation panel — no new transport, no change to the Agent Engine or the viewer platform's own contracts. Also, `SiteAnalysisAgentExecutionCompletionHandler` never existed as planned — the actual completion-reaction mechanism (per the User Story 1 architecture decision, "Option B") is a Hangfire `ContinueJobWith` continuation job, `SiteAnalysisCompletionReactionJob`, invoked directly by `SiteAnalysisChatTurnRouter` rather than any MediatR notification handler.
- [X] T037 [US2] Implement the ambiguous-resolution clarifying-question path (`candidateCount > 1` → assistant asks in chat, no panel dispatched, no TheDigitalCore search) in `src/AskLucy.Application/SiteAnalysis/AgentExecutionCompletion/SiteAnalysisCompletionReactionJob.cs` (depends on T036)
- [X] T038 [US2] Implement `CreateSiteAnalysisProjectLinkFromDeepLinkCommand` + handler (idempotent reuse via `ISiteAnalysisProjectLinkRepository.GetByTheDigitalCoreProjectIdAsync`; creates a new `UserChat` + `SiteAnalysisProjectLink` with `LinkSource = InboundDeepLink` otherwise) in `src/AskLucy.Application/SiteAnalysis/Commands/CreateSiteAnalysisProjectLinkFromDeepLink/` (depends on T012, T009)
- [X] T039 [US2] Implement `SiteAnalysisController` (`POST /api/v1/site-analysis/project-links`, thin, `[Authorize]`, `site-analysis-endpoints` rate-limit policy added to `Program.cs`) in `src/AskLucy.Web/Controllers/v1/SiteAnalysisController.cs` (depends on T038)
- [X] T040 [US2] Implement the minimal SPA deep-link-entry route (`DeepLinkEntry.tsx`: calls the endpoint above, selects the returned conversation via `useActiveConversationStore.setActiveChatId`, redirects into `/studio`, no bespoke chat/viewer UI) at `/site-analysis/enter?projectId=...&siteName=...` in `src/AskLucy.Web/ClientApp/src/features/site-analysis/DeepLinkEntry.tsx` (depends on T039)

**Checkpoint**: User Stories 1 AND 2 both work independently (bootstrap and deep-link entry both land in a boundary-capable conversation).

---

## Phase 5: User Story 3 - Request a Recreation analysis and see cited, scored results (Priority: P2)

**Goal**: With a boundary established, asking for a Recreation analysis produces a cited, scored Floating Panel, relayed to TheDigitalCore.

**Independent Test**: After a boundary is resolved, ask for a Recreation analysis and confirm a Floating Panel with score, findings, and per-finding citations appears; confirm the result was relayed to TheDigitalCore.

### Tests for User Story 3

- [ ] T041 [P] [US3] Unit tests for `SiteAnalysisChatTurnRouter`'s "analyze recreation" trigger recognition in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisChatTurnRouterRecreationTests.cs`
- [ ] T042 [P] [US3] Unit tests for `RelayCategoryScoreResultCommandHandler` (successful relay; retries then surfaces a visible failure on exhaustion, never silently drops — SC-007) in `tests/AskLucy.Application.Tests/SiteAnalysis/RelayCategoryScoreResultCommandHandlerTests.cs`
- [ ] T043 [P] [US3] Contract tests validating a Recreation `AgentExecution.FinalOutputJson` against contracts/site-analysis-category-result.md's shape (citation present on every finding, `dataGaps` array always present) in `tests/AskLucy.Application.Tests/SiteAnalysis/CategoryScoreResultShapeTests.cs`
- [ ] T044 [P] [US3] Frontend unit tests for `SiteAnalysisCategoryResultPanel`'s zod validation and rendering (score, findings-with-citations, malformed-payload fallback) in `src/AskLucy.Web/ClientApp/src/features/site-analysis/panels/SiteAnalysisCategoryResultPanel.test.tsx`
- [ ] T045 [P] [US3] Playwright E2E for a Recreation analysis request end to end (chat ask → panel appears → citation inspectable) in `tests/AskLucy.E2E.Tests/SiteAnalysisRecreationAnalysis.spec.ts` (depends on T016a)

### Implementation for User Story 3

- [ ] T046 [P] [US3] Implement the `collect_recreation_data_layers` MCP tool (wraps notebook Module 02, Recreation-scoped layers, explicit `dataGaps`) in `park-redesign/mcp_server/tools/collect_recreation_data_layers.py` per contracts/site-analysis-mcp-tools.md
- [ ] T047 [US3] Implement the `score_recreation` MCP tool (wraps notebook Module 06 Recreation category; `requiresReview` signal per research.md Decision 9) in `park-redesign/mcp_server/tools/score_recreation.py` (depends on T046)
- [ ] T048 [US3] Register `collect_recreation_data_layers` and `score_recreation` in `park-redesign/mcp_server/server.py` (depends on T047, T015)
- [ ] T049 [US3] Update dev-seed data: activate the two new MCP tools, add them to the Site Analysis Agent's draft tool list, mark `score_recreation`'s `requiresReview` outcome as High-risk in its `AgentPolicy` configuration, and Publish as AgentVersion 2 in `src/AskLucy.Web/DevSeed/` (depends on T048, T016, T016a)
- [ ] T050 [US3] Extend `SiteAnalysisChatTurnRouter` to recognize an "analyze/score recreation" request (requires an existing boundary — FR ordering) in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T035, T049)
- [ ] T051 [US3] Implement `RelayCategoryScoreResultCommand` + handler (calls `ITheDigitalCoreClient.RelayCategoryScoreResultAsync`, retries per this repo's existing HTTP retry convention, surfaces a visible failure on exhaustion) in `src/AskLucy.Application/SiteAnalysis/Commands/RelayCategoryScoreResult/` (depends on T012)
- [ ] T052 [US3] Implement `SiteAnalysisAgentExecutionCompletionHandler`'s category-result branch (read `FinalOutputJson`, dispatch `RelayCategoryScoreResultCommand`, call `IPanelNotifier.PanelRequestedAsync` with `typeKey = "site-analysis-category-result"`) in `src/AskLucy.Application/SiteAnalysis/AgentExecutionCompletion/SiteAnalysisAgentExecutionCompletionHandler.cs` (depends on T036, T051)
- [ ] T053 [P] [US3] Implement `SiteAnalysisCategoryResultPanel` renderer (score, findings with citations, `dataGaps` display, review-pending state, zod-validated payload) in `src/AskLucy.Web/ClientApp/src/features/site-analysis/panels/SiteAnalysisCategoryResultPanel.tsx` per contracts/site-analysis-category-result.md
- [ ] T054 [US3] Register the new `site-analysis-category-result` panel type key with the existing Floating Panel renderer registry in `src/AskLucy.Web/ClientApp/src/viewer/panels/panelRegistry.ts` (depends on T053)

**Checkpoint**: User Stories 1-3 all work independently; Recreation analysis is fully functional.

---

## Phase 6: User Story 4 - Request a Social analysis of the same site (Priority: P2)

**Goal**: A Social-category request, at any point in the conversation, produces its own independent, cited result without disturbing Recreation's.

**Independent Test**: Ask for a Social analysis (with or without a prior Recreation request) and confirm an independent, cited Social panel appears, and any earlier Recreation result remains visible/unaffected.

### Tests for User Story 4

- [ ] T055 [P] [US4] Unit tests for `SiteAnalysisChatTurnRouter`'s "analyze social" trigger recognition, confirming it never also triggers Recreation's tools in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisChatTurnRouterSocialTests.cs`
- [ ] T056 [P] [US4] Unit tests confirming `SiteAnalysisConversationStateAssembler` tracks per-category completion independently (Recreation-done does not affect a later Social request, and vice versa) in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisConversationStateAssemblerTests.cs`
- [ ] T057 [P] [US4] Playwright E2E: request Social after Recreation (and Recreation after Social) and confirm both panels coexist independently in `tests/AskLucy.E2E.Tests/SiteAnalysisSocialAnalysis.spec.ts`

### Implementation for User Story 4

- [ ] T058 [P] [US4] Implement the `collect_social_data_layers` MCP tool (wraps notebook Module 02, Social-scoped layers, explicit `dataGaps`) in `park-redesign/mcp_server/tools/collect_social_data_layers.py`
- [ ] T059 [US4] Implement the `score_social` MCP tool (wraps notebook Module 06 Social category; `requiresReview` signal) in `park-redesign/mcp_server/tools/score_social.py` (depends on T058)
- [ ] T060 [US4] Register `collect_social_data_layers` and `score_social` in `park-redesign/mcp_server/server.py` (depends on T059, T048)
- [ ] T061 [US4] Update dev-seed data: activate the two new MCP tools, add them to the Site Analysis Agent's draft tool list, mark `score_social`'s `requiresReview` outcome as High-risk, and Publish as AgentVersion 3 in `src/AskLucy.Web/DevSeed/` (depends on T060, T049)
- [ ] T062 [US4] Extend `SiteAnalysisChatTurnRouter` to recognize an "analyze/score social" request, independent of any Recreation trigger (FR-011) in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T050, T061)

**Checkpoint**: User Stories 1-4 all work independently; both in-scope categories are fully functional.

---

## Phase 7: User Story 5 - Keep the conversation going, turn by turn, across categories (Priority: P3)

**Goal**: The whole feature behaves as one continuous conversation — each ask handled independently, at the time it's asked, with unrelated messages never interfering.

**Independent Test**: Carry out a single conversation across many turns (boundary → Recreation → unrelated question → Social → Recreation again) and confirm each ask is scoped to only what it needs, with no cross-turn interference or stale-result reuse.

### Tests for User Story 5

- [ ] T063 [P] [US5] Playwright E2E for a full multi-turn conversation (bootstrap → boundary → Recreation → unrelated question → Social → Recreation again) confirming independent scoping and no interference in `tests/AskLucy.E2E.Tests/SiteAnalysisTurnByTurn.spec.ts`
- [ ] T064 [P] [US5] Unit tests confirming the router no-ops (passes through to an ordinary chat reply) for a message matching none of this feature's trigger conditions in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisChatTurnRouterNoOpTests.cs`

### Implementation for User Story 5

- [ ] T065 [US5] Implement "repeated category request always starts a new `AgentExecution` and refreshes the result" behavior (no silent reuse of a stale prior result) in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T062)
- [ ] T066 [US5] Confirm/harden the "no matching trigger condition → pass through untouched" guard so an unrelated message between category requests never resets or interferes with established state (contracts/chat-to-agent-routing.md's explicit non-goals) in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T065)
- [ ] T067 [US5] Implement the out-of-scope-category reply path (Environmental/Sustainability/Accessibility/Safety/Smart City request → clear "not yet supported" chat reply, never silently attempted) in `src/AskLucy.Application/SiteAnalysis/Routing/SiteAnalysisChatTurnRouter.cs` (depends on T066)

**Checkpoint**: All 5 conversational stories work independently and together as one continuous conversation.

---

## Phase 8: User Story 6 - Transparent handling of data gaps and conflicting results (Priority: P3)

**Goal**: Missing data or conflicting tool results are always visibly surfaced or paused for approval — never silently resolved.

**Independent Test**: Force a missing-data-layer scenario and confirm a visible data-gap notice; force a conflicting-result scenario and confirm the execution pauses for human approval.

### Tests for User Story 6

- [ ] T068 [P] [US6] Unit tests confirming a tool-reported `dataGaps` entry always surfaces in the completed `FinalOutputJson` and is never dropped in `tests/AskLucy.Application.Tests/SiteAnalysis/DataGapSurfacingTests.cs`
- [ ] T069 [P] [US6] Unit tests confirming a `requiresReview: true` tool outcome results in the `AgentExecution` pausing in `WaitingForApproval` (existing `specs/020` mechanism) rather than completing in `tests/AskLucy.Application.Tests/SiteAnalysis/RequiresReviewApprovalGatingTests.cs`
- [ ] T070 [P] [US6] Playwright E2E forcing a data-gap scenario and a conflicting-result scenario, confirming the visible gap notice and the approval pause respectively in `tests/AskLucy.E2E.Tests/SiteAnalysisDataGapAndConflict.spec.ts`

### Implementation for User Story 6

- [ ] T071 [US6] Ensure `SiteAnalysisAgentExecutionCompletionHandler` never filters out or summarizes away a `dataGaps` entry before relaying to TheDigitalCore or notifying the panel (constitution §2.VIII) in `src/AskLucy.Application/SiteAnalysis/AgentExecutionCompletion/SiteAnalysisAgentExecutionCompletionHandler.cs` (depends on T052)
- [ ] T072 [US6] Ensure `SiteAnalysisCategoryResultPanel` visibly renders `dataGaps` and the review-pending state as distinct from a normal completed score, never blending them into the score display (frontend) in `src/AskLucy.Web/ClientApp/src/features/site-analysis/panels/SiteAnalysisCategoryResultPanel.tsx` (depends on T053)

**Checkpoint**: All 6 user stories are independently functional and satisfy their guardrail requirements.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, security review, and end-to-end validation across all stories.

- [ ] T073 [P] Update `docs/ARCHITECTURE.md`, `docs/ENTITY_MODEL.md`, and `docs/DATABASE.md` to describe the new `SiteAnalysis` module (constitution §13 — documentation is part of implementation)
- [ ] T074 [P] Security review: confirm the TheDigitalCore service-account credential never reaches the `ClientApp` bundle or logs, and confirm the new `SiteAnalysisController` endpoint has rate limiting applied (constitution §8)
- [ ] T075 [P] Finalize `docs/adr/0008-park-redesign-mcp-server-dependency.md` and `docs/adr/0009-thedigitalcore-service-account-integration.md` with any details that changed during implementation (depends on T001, T002, and all prior phases)
- [ ] T076 Run all 7 of quickstart.md's validation scenarios end to end and record results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3-8)**: All depend on Foundational phase completion.
  - US1 (P1) and US2 (P1) can proceed in parallel once Foundational is done (US2's boundary-render code depends on US1's completion-handler skeleton existing, T026 — see per-story notes below — so in practice US1 lands first).
  - US3 (P2) depends on US2 (needs boundary + `SiteAnalysisConversationStateAssembler`, T034/T035).
  - US4 (P2) depends on US3 (extends the same router file, T050/T062, and reuses the same MCP-server/DevSeed publish pattern).
  - US5 (P3) depends on US3+US4 (validates turn-by-turn behavior across both categories).
  - US6 (P3) depends on US3 (needs a completed category result to attach guardrail behavior to).
- **Polish (Phase 9)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Foundational only. No dependency on other stories — this is the MVP.
- **User Story 2 (P1)**: Foundational + reuses US1's `SiteAnalysisChatTurnRouter`/`SiteAnalysisAgentExecutionCompletionHandler` skeletons (T024, T026) as extension points, but is independently testable via a deep-link entry that never goes through US1's bootstrap flow at all (T038-T040).
- **User Story 3 (P2)**: Requires US2's resolved boundary and conversation-state assembly (T034/T035) to exist.
- **User Story 4 (P2)**: Requires US3's router/DevSeed/server.py extension points (T050, T049, T048) to extend rather than duplicate.
- **User Story 5 (P3)**: Requires US3+US4's category triggers to exist to validate turn-by-turn behavior across them.
- **User Story 6 (P3)**: Requires US3's completion-handler/panel to exist to attach data-gap/review-pending rendering to.

### Within Each User Story

- Tests are written before implementation and MUST fail first.
- MCP tools (Python) before their registration in `server.py` before DevSeed activation before the router recognizes the corresponding trigger.
- Domain/Persistence before Application before Infrastructure wiring before Web/frontend.
- Story complete before moving to the next priority.

### Parallel Opportunities

- All Setup tasks (T001-T004) can run in parallel.
- Within Foundational, T005→T009 (persistence chain) and T010→T013 (TheDigitalCore client chain) and T014→T016 (Python/MCP chain) are three independent chains that can proceed in parallel with each other.
- All test tasks marked [P] within a story can run in parallel with each other.
- `SiteAnalysisCategoryResultPanel` (T053) and its registration (T054) can proceed in parallel with backend category-result wiring (T051-T052) since they only share the JSON contract, not code.
- Different user stories' MCP-tool implementation tasks (e.g., T046/T047 for US3 vs T058/T059 for US4) touch different files and could be staffed in parallel once US2 is done, even though US4's router/DevSeed tasks are sequenced after US3's for review-clarity.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for SiteAnalysisChatTurnRouter's bootstrap-trigger recognition in tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisChatTurnRouterBootstrapTests.cs"
Task: "Unit tests for VerifyAndLinkDigitalCoreProjectCommandHandler in tests/AskLucy.Application.Tests/SiteAnalysis/VerifyAndLinkDigitalCoreProjectCommandHandlerTests.cs"
Task: "Integration tests for TheDigitalCoreClient in tests/AskLucy.Infrastructure.Tests/SiteAnalysis/TheDigitalCoreClientTests.cs"
Task: "Domain tests for SiteAnalysisProjectLink invariants in tests/AskLucy.Domain.Tests/SiteAnalysis/SiteAnalysisProjectLinkTests.cs"
Task: "Playwright E2E for the full bootstrap flow in tests/AskLucy.E2E.Tests/SiteAnalysisBootstrap.spec.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: run quickstart.md Scenarios 1-2 independently.
5. Demo: a user can describe a real park and get a digital Project bootstrapped in TheDigitalCore, entirely conversationally.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. User Story 1 → validate independently → MVP.
3. User Story 2 → validate independently (adds boundary rendering + deep-link entry).
4. User Story 3 → validate independently (adds the first real analysis category).
5. User Story 4 → validate independently (adds the second category, proves independence).
6. User Story 5 → validate the full multi-turn conversation shape.
7. User Story 6 → validate the guardrail behaviors end to end.
8. Polish.

### Parallel Team Strategy

With multiple developers, once Foundational is done: one developer can own the
`SiteAnalysisChatTurnRouter`/completion-handler chain (US1→US2→US5 build on each other, best kept
sequential/single-owner), while a second develops the Python MCP tools for US3/US4 in the sibling
repo (independent files, only the JSON contract needs to match), and a third builds the frontend
Floating Panel renderer (T053-T054) against the same contract in parallel with the backend relay
wiring.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- Two repositories are in play — `park-redesign/mcp_server/**` tasks are Python, everything else is this repo.
- No Agent Engine, MCP Tool Engine, Immersive Viewer, or Floating Panel *core* mechanism is modified anywhere in this task list — every touch point is additive (new tool rows, new panel type key, new router file), per plan.md's Constitution Check.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently before continuing.
