---
description: "Task list for SPEC-057 Site Analysis Agent"
---

# Tasks: Site Analysis Agent

**Input**: Design documents from `/specs/057-site-analysis-agent/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included. The constitution (§10) requires tests in the same change that introduces behavior.

**Organization**: Grouped by user story so each can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story the task belongs to (US1–US5)
- Every task names its exact file path

---

## ⚠️ READ THIS FIRST — 7 rules that override intuition

These are verified facts about this codebase. Each one is a trap that looks correct but is not. Violating any
of them produces a feature that appears to work and does not.

| # | Rule | Why |
|---|---|---|
| 1 | Specialists call the relay **inline**, as the last step of their own `ExecuteAsync`. **Never** deliver findings via workflow node-completion events. | `WorkflowExecutionOrchestrator.ExecuteParallelAsync` collects notifications into `pendingNotifications` and flushes them only after **every** branch settles (`WorkflowExecutionOrchestrator.cs:948-973`). Using them delivers everything at once. |
| 2 | Branch nodes are **`NativeTool`**, never `AiAgent`. | `AgentNodeExecutor` resolves agents with `GetByIdForOwnerAsync(agentId, context.UserId)` (`AgentNodeExecutor.cs:61`) which cannot find system-owned agents, and nulls the chat link (`:94`). |
| 3 | `WorkflowExecution.Create(..., runByUserId: ...)` must receive the **real signed-in user id**, never `"system"`. | `IPanelNotifier` and `ISiteAnalysisNotifier` both key by user id. A system id pushes to nobody. |
| 4 | Never give the client the provider's image URL. Persist bytes as a `Document`, pass `fileId`. | `imageBlockSchema` rejects external addresses (`blocks.ts:80-88`); `ImageBlockRenderer` resolves via `/documents/{fileId}/download`. |
| 5 | The Merge node must use merge strategy **`AnyCompleted`**. | It is the engine's only branch-failure tolerance (`WorkflowExecutionOrchestrator.cs:989`). Otherwise one failed specialist fails the whole analysis. |
| 6 | Never compute confidence from `GeocodingCandidate.Importance`. | Google and Nominatim populate it on incompatible scales; this already caused a production defect. Use the rule table in `data-model.md`. |
| 7 | The workflow provisioner must **defer when migrations are pending**. | Mirror `SystemAgentProvisioner.cs:50` and `:64`, or a fresh database crashes at startup. |

**Workflow graph shape** (fixed by the engine): `Start → Parallel → [exactly one node per specialist] → Merge → End`.
Branches must be **exactly one node long** and all converge on the **same** Merge node
(`WorkflowExecutionOrchestrator.cs:876-883`).

**Enum serialization**: enums serialize as **strings** API-wide via an existing `Program.cs` converter. Do not
add `[JsonConverter]` attributes and do not compare numerically on the client.

---

## Phase 1: Setup

**Purpose**: Configuration scaffolding needed by later phases.

- [X] ~~T001 Create `src/AskLucy.Application/Options/SiteAnalysisOptions.cs`...~~ **Reverted (2026-09-18).**
- [X] ~~T002 Bind and validate `SiteAnalysisOptions` at startup in `src/AskLucy.Web/Program.cs`...~~ **Reverted (2026-09-18).**

> **Both tasks were done, then undone.** `SiteAnalysisOptions` put the image provider and model in
> `appsettings`, bypassing the AI-capability assignments every other AI task already uses — an administrator
> could not change the model without a redeploy, and the platform had two competing ways to pick a model.
> The class and its binding were deleted; selection moved to the `ImageGeneration` capability assignment
> (see research.md D7's superseded note). `Ai:OpenAI:ImageModel` was removed from `appsettings.json` for the
> same reason. If you are following this file as a build order, skip both tasks.
>
> A required `IOptions<T>` with `ValidateOnStart()` and no default is also worth avoiding on its own terms:
> it fails the entire host, not just its own feature, and that failure is invisible to `dotnet build` and to
> unit tests — it only surfaces on a real host boot.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain, persistence, and push infrastructure that every user story depends on.

**⚠️ No user story work may begin until this phase is complete.**

### Domain

- [X] T003 [P] Create `src/AskLucy.Domain/SiteAnalysis/SiteAnalysisEnums.cs` containing four enums exactly as specified in [data-model.md](./data-model.md) § Enums: `SiteAnalysisStatus` (Running, Completed, Failed), `SiteAnalysisResultStatus` (Completed, Failed, Rejected), `SiteAnalysisConfidenceLevel` (High, Medium, Low), `SiteAnalysisType` (SchematicImage only — the other five are deferred).
- [X] T004 Create `src/AskLucy.Domain/SiteAnalysis/SiteAnalysisResult.cs` with the fields listed in [data-model.md](./data-model.md) § SiteAnalysisResult. Provide two static factories — `Completed(...)` and `Failed(...)` / `Rejected(...)` — enforcing the invariant that **exactly one** of (`ContentJson` + `DataSource` + `ConfidenceLevel`) or `FailureReason` is populated. Throw `DomainRuleViolationException` on violation. Inherit `BaseEntity`. No EF attributes (constitution §3 domain purity).
- [X] T005 Create `src/AskLucy.Domain/SiteAnalysis/SiteAnalysis.cs` as the aggregate root with the fields in [data-model.md](./data-model.md) § SiteAnalysis and a private `List<SiteAnalysisResult>` exposed as `IReadOnlyCollection`. Implement: `Create(...)`, `RecordDispatched(workflowExecutionId, actor)`, `AddResult(...)`, `AddFailedResult(...)`, `TryClaimClosingOutcome(actor) → bool` (sets `ClosingOutcomeReportedAtUtc` only if currently null, returns whether this caller won — this is what enforces FR-027 under concurrent branch completion), `Complete(actor)`, `Fail(reason, actor)`. Use `Guid.CreateVersion7()` for ids. Model `Workflow.cs` for style.
- [X] T006 [P] Add `IsSystemOwned` (bool, defaults false), `SystemKey` (string?), and a `CreateSystemProvisioned(systemKey, name, description, workflowType, actor)` factory to `src/AskLucy.Domain/Workflows/Workflow.cs`. Copy the pattern from `src/AskLucy.Domain/Agents/Agent.cs:102-124` (fields) and `:338-377` (factory) exactly. Set `OwnerId` to the existing `"system"` convention (`Agent.SystemOwnerId`). Do not change any existing `Workflow` member.

### Persistence

- [X] T007 [P] Create `src/AskLucy.Persistence/Configurations/SiteAnalysisConfiguration.cs` and `SiteAnalysisResultConfiguration.cs`. Configure the owned-collection relationship with cascade delete, the soft-delete global query filter (match an existing configuration such as `AttachmentConfiguration.cs`), and the indexes listed in [data-model.md](./data-model.md) § Persistence: `(UserId, UserChatId, StartedAtUtc DESC)`, `(SiteAnalysisId)`, and unique `(SiteAnalysisId, AnalysisType)`.
- [X] T008 [P] Add a filtered unique index on `Workflow.SystemKey` (unique where not null) in the existing workflow EF configuration under `src/AskLucy.Persistence/Configurations/`. Mirror the equivalent index already defined for `Agent.SystemKey`.
- [X] T009 Add `DbSet<SiteAnalysis>` to `src/AskLucy.Persistence/AskLucyDbContext.cs`. Do **not** add a `DbSet<SiteAnalysisResult>` — children are reachable only through the aggregate (constitution §5).
- [X] T010 Create `src/AskLucy.Application/Abstractions/ISiteAnalysisRepository.cs` with aggregate-oriented methods only (no `IQueryable`): `Add(SiteAnalysis)`, `GetByIdAsync(Guid id, CancellationToken)`, `GetByIdForUserAsync(Guid id, string userId, CancellationToken)`, `ListByChatAsync(string userId, Guid userChatId, CancellationToken)`, and `FindRunningForSiteAsync(string userId, Guid userChatId, string siteName, CancellationToken)` returning the in-flight analysis for that chat and site, or null (needed by T031 for duplicate-request reuse). Mirror `IAgentRepository.cs`.
- [X] T011 Implement `src/AskLucy.Persistence/Repositories/SiteAnalysisRepository.cs`. Include the results collection with explicit `Include`, project nothing (this returns the aggregate). Mirror `src/AskLucy.Persistence/Repositories/AgentRepository.cs`.
- [X] T012 Add `GetBySystemKeyAsync(string systemKey, CancellationToken)` to `src/AskLucy.Application/Abstractions/IWorkflowRepository.cs` and implement it in the corresponding repository under `src/AskLucy.Persistence/Repositories/`. Mirror `IAgentRepository.GetBySystemKeyAsync` and its implementation exactly.
- [X] T013 Generate one EF Core migration creating both new tables, the two `Workflow` columns, and the filtered unique index: `dotnet ef migrations add AddSiteAnalysis --project src/AskLucy.Persistence --startup-project src/AskLucy.Web`. Verify the generated `Down` method is complete and reversible (constitution §5). Check the file has no BOM before committing.

### Push infrastructure

- [X] T014 [P] Create `src/AskLucy.Application/SiteAnalysis/SiteAnalysisNotificationDtos.cs` containing `SiteAnalysisResultReceivedDto` and `SiteAnalysisCompletedDto` with exactly the fields documented in [contracts/site-analysis-hub-events.md](./contracts/site-analysis-hub-events.md).
- [X] T015 [P] Create `src/AskLucy.Application/Abstractions/ISiteAnalysisNotifier.cs` with `ResultReceivedAsync(string userId, SiteAnalysisResultReceivedDto, CancellationToken)` and `AnalysisCompletedAsync(string userId, SiteAnalysisCompletedDto, CancellationToken)`. Mirror `src/AskLucy.Application/Abstractions/IPanelNotifier.cs:14-17`. Application must not reference SignalR (constitution §3).
- [X] T016 Create `src/AskLucy.Infrastructure/SiteAnalysis/SiteAnalysisHub.cs` — copy `src/AskLucy.Infrastructure/Panels/PanelHub.cs` verbatim, changing only the class name and XML doc. Keep `[Authorize]`, the `UserGroup(userId)` helper, and the `OnConnectedAsync` group join reading `ClaimTypes.NameIdentifier` from the **server-verified** token (never client-supplied).
- [X] T017 Create `src/AskLucy.Infrastructure/SiteAnalysis/SiteAnalysisNotifier.cs` implementing `ISiteAnalysisNotifier` over `IHubContext<SiteAnalysisHub>`, sending event names `"SiteAnalysisResultReceived"` and `"SiteAnalysisCompleted"`. Copy the shape of `src/AskLucy.Infrastructure/Panels/PanelNotifier.cs`.
- [X] T018 Register the hub route `app.MapHub<SiteAnalysisHub>("/hubs/site-analysis");` in `src/AskLucy.Web/Program.cs` beside the existing `MapHub` calls at lines 661-666.
- [X] T019 Register in `src/AskLucy.Infrastructure/DependencyInjection.cs`: `ISiteAnalysisNotifier → SiteAnalysisNotifier`, and `ISiteAnalysisRepository → SiteAnalysisRepository` in the persistence DI file. Use lifetimes consistent with neighbouring registrations (scoped for repositories).
- [X] T020 Add a `SiteAnalysis` member to the `SubAgentArea` enum in `src/AskLucy.Application/Conversations/Capabilities/IConversationCapability.cs:28`. Additive only — do not reorder or remove existing members.

**Checkpoint**: Domain, persistence, and push plumbing exist. `dotnet build` passes. User story work can begin.

---

## Phase 3: User Story 1 — Ask for a site analysis and watch findings arrive (P1) 🎯 MVP

**Goal**: A user asks Lucy to analyze a site; dispatch returns immediately; when the specialist finishes, a
chat notice and a floating panel appear — delivered through the parent, not by the specialist.

**Independent test**: Ask for an analysis of an already-resolved site. The acknowledgement returns before any
finding is ready, and the notice + panel appear on their own when the specialist finishes
([quickstart.md](./quickstart.md) Scenarios 1–2).

### Coordination core

- [X] T021 [P] [US1] Create `src/AskLucy.Application/SiteAnalysis/SiteAnalysisResultMetadata.cs` — the record defined in [contracts/result-relay.md](./contracts/result-relay.md) § Interface.
- [X] T022 [P] [US1] Create `src/AskLucy.Application/SiteAnalysis/SiteAnalysisContentComposer.cs`, a plain Application class that builds a content-block document from a heading, body blocks, and a provenance key/value block. Emit only block kinds that already exist in `src/AskLucy.Web/ClientApp/src/viewer/panels/content/blocks.ts`: `heading`, `text`, `keyValue`, `image`, `divider`. Do **not** invent a new block kind.
- [X] T023 [US1] Create `src/AskLucy.Application/SiteAnalysis/ISiteAnalysisResultRelay.cs` with the two methods in [contracts/result-relay.md](./contracts/result-relay.md).
- [X] T024 [US1] Implement `src/AskLucy.Application/SiteAnalysis/SiteAnalysisResultRelay.cs` — **the parent**. `ReportSuccessAsync` must, in order: (1) run every check in the contract's validation table, persisting as `Rejected` and returning if any fails; (2) persist the result as `Completed` and commit via `IUnitOfWork`; (3) push `SiteAnalysisResultReceived` via `ISiteAnalysisNotifier`; (4) push the panel via `IPanelNotifier.PanelRequestedAsync(userId, PanelRequestDto.ForContent(...))`. Resolve the owning chat and user from the **`SiteAnalysis` record**, never from the tool context (rule 2). Fire notifications **after** the commit; a push failure must be logged but must not roll back the persisted result.
- [X] T025 [US1] Create `src/AskLucy.Application/SiteAnalysis/ISiteAnalysisDispatcher.cs` and `SiteAnalysisDispatcher.cs`. It must: look up the workflow via `IWorkflowRepository.GetBySystemKeyAsync("site-analysis")`, resolve its published version, call `WorkflowExecution.Create(workflowId, workflowVersionId, runByUserId, WorkflowExecutionTriggerType.EventDriven, triggeringEventReferenceJson, inputsJson, actor)`, persist, and call `IWorkflowExecutionRunner.EnqueueAsync(executionId, ct)`. `runByUserId` **must be the real signed-in user** (rule 3). Pass `siteAnalysisId`, `siteName`, `siteLocation`, `latitude`, `longitude` in `inputsJson`. Do **not** route through `StartWorkflowExecutionCommand` — its ownership guard rejects system workflows (research D12).

### The schematic image specialist

- [X] T026 [US1] Verify `IDocumentFileValidator` accepts PNG. Locate its accepted-type set and confirm `DocumentFileType.Png` passes magic-byte validation; extend it if image uploads were previously restricted to document formats. This is the open verification item in [contracts/schematic-image-prompt.md](./contracts/schematic-image-prompt.md) and blocks T027.
- [X] T027 [US1] Implement `src/AskLucy.Application/SiteAnalysis/Tools/SiteSchematicImageGenerationTool.cs` as an `IAgentTool` (`src/AskLucy.Application/Agents/Tools/IAgentTool.cs:56-71`). Sequence: read `siteAnalysisId`/`siteName`/`siteLocation` from the input JSON → build the prompt from the v1 template in [contracts/schematic-image-prompt.md](./contracts/schematic-image-prompt.md) (load it as a versioned artifact, **not** an inline literal — constitution §9) → `IImageGenerationService.GenerateAsync(prompt, ct)` (which resolves the `ImageGeneration` capability assignment — provider **and** pinned model — and normalises whatever form the provider returns to bytes; see research.md D7's superseded note) → `DocumentUploadFinalizer.FinalizeAsync(ownerId: analysis.UserId, fileName, content, sizeBytes, actor, ct)` → compose content via `SiteAnalysisContentComposer` with an `image` block carrying the returned `fileId` and meaningful `alt` text → **call `ISiteAnalysisResultRelay.ReportSuccessAsync` inline** (rule 1) → return `AgentToolResult.Success`. Never return the provider URL (rule 4).
- [X] T028 [US1] Register `SiteSchematicImageGenerationTool` in DI so `AgentToolCatalog` can resolve it by name — `NativeToolNodeExecutor` looks it up via `toolCatalog.Find(toolName)` (`NativeToolNodeExecutor.cs:36`). Follow how existing `IAgentTool` implementations are registered in `src/AskLucy.Infrastructure/DependencyInjection.cs`.

### Workflow provisioning

- [X] T029 [US1] Create `src/AskLucy.Infrastructure/SiteAnalysis/SystemWorkflowProvisioner.cs`, mirroring `src/AskLucy.Infrastructure/Conversations/SystemAgentProvisioner.cs`. It seeds and publishes a workflow with `SystemKey = "site-analysis"` shaped `Start → Parallel → [one NativeTool node per specialist] → Merge → End`. This release has **one** branch node, configured `{"toolName":"<tool name>","input":{...}}`. Set the Merge node's strategy to **`AnyCompleted`** (rule 5). Nodes must be `NativeTool`, never `AiAgent` (rule 2). Copy the defer-on-pending-migrations guard from `SystemAgentProvisioner.cs:50` and `:64` (rule 7), and the concurrent-creation race handling at `:120-131`.
- [X] T030 [US1] Create `src/AskLucy.Infrastructure/SiteAnalysis/SystemWorkflowProvisioningHostedService.cs` and register it with `services.AddHostedService<...>()` in `src/AskLucy.Infrastructure/DependencyInjection.cs`. Copy `SystemAgentProvisioningHostedService.cs` exactly, including its scope creation (the provisioner and its repositories are scoped while the hosted service is a singleton).

### Lucy's entry point

- [X] T031 [US1] Implement `src/AskLucy.Application/Conversations/Capabilities/RequestSiteAnalysisCapability.cs` as an `IConversationCapability` (all members listed in `IConversationCapability.cs:74-152`). Set `Area => SubAgentArea.SiteAnalysis` and `ExpectedDuration => CapabilityDuration.Extended`. `ExecuteAsync` must, in order:
  1. **Resolve the site (FR-001, FR-002)** — accept **either** a place name **or** explicit coordinates. If the input parses as a coordinate pair, use it directly and skip geocoding entirely. Otherwise resolve from `chat.ActiveLocation`/`ActiveBoundary` the way `OpenSolarAnalysisCapability.cs` does, falling back to `IBoundaryResolutionService` exactly as `src/AskLucy.Application/Agents/Tools/SiteBoundaryResolverTool.cs` does. Do **not** write a third resolution path. State both accepted forms in `ArgumentHint`.
  2. **Reuse an in-flight analysis (FR-001 edge case, spec Assumptions)** — call `ISiteAnalysisRepository.FindRunningForSiteAsync(...)`. If one exists, return its acknowledgement and **do not** dispatch a second analysis. Without this, asking twice produces duplicate panels.
  3. Create and persist the `SiteAnalysis` record with `ExpectedResultCount = 1`.
  4. Call the dispatcher, then **return immediately** with the acknowledgement. It must not await the workflow.

  If the site cannot be resolved, return a failure the user sees in the same turn (FR-023).
- [X] T032 [US1] Register `RequestSiteAnalysisCapability` in DI alongside the other capabilities so `ConversationCapabilityCatalog` discovers it.

### Client delivery

- [X] T033 [P] [US1] Create `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/api/siteAnalysisApi.ts` with TypeScript types mirroring the DTOs in [contracts/site-analysis-hub-events.md](./contracts/site-analysis-hub-events.md) and [contracts/site-analysis-api.md](./contracts/site-analysis-api.md). Enums are **string unions**, never numbers.
- [X] T034 [US1] Create `src/AskLucy.Web/ClientApp/src/features/siteAnalysis/hooks/useSiteAnalysisHub.ts`, modelled on `src/AskLucy.Web/ClientApp/src/viewer/panels/hooks/useFloatingPanelHub.ts:22-62`. Connect to `/hubs/site-analysis`; on `SiteAnalysisResultReceived`, render `noticeText` as an assistant message in `userChatId`; ignore a repeat of a `resultId` already seen (SignalR can redeliver). **Use the same access-token delivery mechanism the other six hub hooks use** — a stale closure over the token is a known past defect in this codebase. Every async path needs an explicit error path with visible UI feedback; no console-only failures, no unhandled rejections (constitution §2 VIII).
- [X] T035 [US1] Mount `useSiteAnalysisHub` in the component that already hosts the other hub hooks for a signed-in session, so notices arrive without the viewer being open.

### Tests

- [X] T036 [P] [US1] Add `tests/AskLucy.Domain.Tests/SiteAnalysis/SiteAnalysisTests.cs` covering: the exactly-one-of invariant on results, `TryClaimClosingOutcome` returning true exactly once across repeated calls, and state transitions.
- [X] T037 [P] [US1] Add `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisResultRelayTests.cs` covering the success path: valid result → persisted `Completed` → notifier called → panel pushed, in that order, with all dependencies faked.
- [X] T038 [P] [US1] Add `tests/AskLucy.Application.Tests/SiteAnalysis/SiteAnalysisDispatcherTests.cs` asserting `runByUserId` is the caller's id and never `"system"` (rule 3), and that `EnqueueAsync` is called.
- [X] T039 [P] [US1] Add `tests/AskLucy.Application.Tests/SiteAnalysis/RequestSiteAnalysisCapabilityTests.cs` asserting: it returns **without awaiting** the workflow (this is the assertion that satisfies SC-001 — the dispatch path must not block on specialist work); it returns an in-turn failure when the site cannot be resolved (FR-023); it accepts **explicit coordinates** and skips geocoding for them (FR-001); and a second request while one is `Running` for the same chat and site **reuses** the in-flight analysis and dispatches only once.

**Checkpoint**: MVP complete — quickstart Scenarios 1, 2 and 3 pass.

---

## Phase 4: User Story 2 — Every finding carries its provenance (P1)

**Goal**: Each finding shows what produced it, from which source, and with what confidence; figures with no
data source say so rather than being invented.

**Independent test**: Open a delivered finding and confirm analysis type, data source, and confidence are all
shown ([quickstart.md](./quickstart.md) Scenario 4).

- [X] T040 [P] [US2] Create `src/AskLucy.Application/SiteAnalysis/Providers/IZoningDataProvider.cs`, `IFloodDataProvider.cs`, and `IClimateDataProvider.cs`, plus a shared outcome type whose vocabulary mirrors `SiteBoundaryResolverTool`'s `confirmed|no_candidates|ambiguous|unavailable`. **Interfaces and the outcome type only** — no implementations, no DTOs, no configuration, no DI registration (constitution §2 III; scope justified in plan.md Complexity Tracking).
- [X] T041 [US2] Extend `SiteAnalysisContentComposer` (T022) with a `BuildProvenanceBlock(SiteAnalysisResultMetadata)` method emitting a `keyValue` block containing analysis type, data source, confidence level, and generation time (FR-011). Every specialist appends it as the final block.
- [X] T042 [US2] Add a documented `ResolveConfidence(...)` helper next to the composer implementing the rule table in [data-model.md](./data-model.md) § Confidence assignment rule. It must take grounding facts as parameters, never a numeric score. Add a comment stating that `GeocodingCandidate.Importance` must never be used here (rule 6).
- [X] T043 [US2] Make `SiteSchematicImageGenerationTool` (T027) report `DataSource = "{providerKey}:{model}"` and `ConfidenceLevel = Medium`, per [contracts/schematic-image-prompt.md](./contracts/schematic-image-prompt.md) § Provenance reported.
- [X] T044 [P] [US2] Extend `SiteAnalysisResultRelayTests` to assert a result missing `DataSource` or `ConfidenceLevel` is persisted as `Rejected` and is **not** delivered.

**Checkpoint**: Findings are self-describing and honest about their grounding.

---

## Phase 5: User Story 3 — Findings survive leaving the page (P1)

**Goal**: Completed findings are retrievable after navigation or reload.

**Independent test**: Start an analysis, navigate away, return after completion, and find the results present
([quickstart.md](./quickstart.md) Scenario 5).

- [X] T045 [P] [US3] Create `src/AskLucy.Application/SiteAnalysis/Queries/GetSiteAnalysis/GetSiteAnalysisQuery.cs`, `GetSiteAnalysisQueryHandler.cs`, and the response DTOs, returning exactly the 200 OK shape in [contracts/site-analysis-api.md](./contracts/site-analysis-api.md). Filter by the current user (FR-019). **Do not expose `failureReason`** — it is operator-only diagnostic detail.
- [X] T046 [US3] Create `src/AskLucy.Web/Controllers/SiteAnalysesController.cs` exposing `GET /api/v1/site-analyses/{id}`. `[Authorize]`. Return **404** (not 403) when the analysis belongs to another user — existence must not be disclosed. Errors as RFC 7807 Problem Details. Ensure it appears in the OpenAPI document (constitution §6).
- [X] T047 [P] [US3] Add a `ListByChatAsync`-backed query and endpoint, or extend the client to fetch analyses for the open conversation — whichever fits the existing chat-open data flow. Keep results bounded; no unbounded list (constitution §6).
- [X] T048 [US3] In the client, fetch the conversation's analyses on chat open via TanStack Query and reopen each completed result's panel with `floatingPanelStore.openPanel({ kind: 'content', ... })`, replaying the **stored** content document so it renders identically to first delivery (FR-018). Surface fetch failures through the query's error state with a retry affordance — never console-only.
- [X] T049 [P] [US3] Add `tests/AskLucy.Web.Tests/SiteAnalysis/GetSiteAnalysisTests.cs` asserting 200 for the owner, 404 for another user, 401 unauthenticated, and that `failureReason` is absent from the response. Requires `PERSISTENCE_TESTS_CONNECTION_STRING`.

**Checkpoint**: A multi-minute analysis is safe to walk away from.

---

## Phase 6: User Story 4 — A failing specialist does not sink the analysis (P2)

**Goal**: One specialist failing leaves the others intact; every failure is captured and diagnosable; the
analysis always ends with exactly one user-visible outcome.

**Independent test**: Misconfigure the image provider, run an analysis, confirm no failure notice reaches the
user but the failure is fully recorded and the analysis still ends ([quickstart.md](./quickstart.md)
Scenarios 6 and 7).

> Quickstart Scenario 9 (one failure among several does not sink the rest) also exercises this phase's work,
> but it needs a second specialist and therefore runs in Phase 7. It is **not** part of this phase's
> independent test.

- [X] T050 [US4] Implement `ReportFailureAsync` in `SiteAnalysisResultRelay` per [contracts/result-relay.md](./contracts/result-relay.md) § Behavior on failure: persist as `Failed` with the reason **and** the originating error detail; log via `ILogger<T>` with structured properties (analysis id, site name, analysis type, user id, exception); push **nothing**. Never swallow the exception — capture is what constitution §2 VIII requires (research D13).
- [X] T051 [US4] Add failure handling to `SiteSchematicImageGenerationTool`: catch `NotSupportedException` and the existing `AiProviderException` subtypes (`Unavailable`, `CredentialRejected`, `RateLimited`, `QuotaExhausted`, `UsageRestricted`, `RequestInvalid`, `ResponseNotUnderstood`), report via `ReportFailureAsync`, and return `AgentToolResult.Failure`. **Do not introduce new exception types** (FR-030).
- [X] T052 [US4] Implement settlement in the relay: after each result is persisted, if the settled count equals `ExpectedResultCount`, call `TryClaimClosingOutcome` and — only if the claim is won — set the analysis terminal state and emit `SiteAnalysisCompleted` with the `noticeText` rules in [contracts/site-analysis-hub-events.md](./contracts/site-analysis-hub-events.md) (`null` when all succeeded; partial text when some failed; failure text when none succeeded).
- [X] T053 [US4] Handle `DbUpdateConcurrencyException` in the relay by re-reading and conceding the closing-outcome claim, never letting it surface as a 500 (constitution §5).
- [X] T054 [US4] In the client hook, render `SiteAnalysisCompleted.noticeText` as an assistant message **only when non-null**, and do nothing when null.
- [X] T055 [P] [US4] Extend `SiteAnalysisResultRelayTests` with: a failed report produces no notifier or panel call; the closing outcome fires exactly once under two simultaneous final reports; all-failed sets `Failed` and emits the could-not-complete text; all-succeeded emits `noticeText: null`.

**Checkpoint**: The feature degrades honestly and is diagnosable when it does.

---

## Phase 7: User Story 5 — A new specialist can be added without redesign (P3)

**Goal**: Prove progressive arrival and the extensibility contract (FR-031).

**Independent test**: Add a throwaway specialist and confirm it is delivered, validated, persisted, and
rehydrated with no change to shared logic ([quickstart.md](./quickstart.md) Scenario 8).

- [X] T056 [US5] Temporarily add a throwaway `SiteAnalysisType` member and a trivial `IAgentTool` that delays ~10 s then reports a one-block finding through the relay. Register it and add one branch node to `SystemWorkflowProvisioner`.
- [X] T057 [US5] Run an analysis and confirm the two findings arrive at **different** timestamps, each when its own specialist finished. This is the observation that satisfies SC-002. If they arrive together, rule 1 has been violated — the implementation is riding batched workflow node events.
- [X] T058 [US5] Confirm adding the specialist required **no** change to the relay, dispatcher, repository, notifier, or client. Record any file that did need changing as an FR-031 defect and fix it.
- [X] T059 [US5] With the throwaway specialist still present, break image generation and confirm the analysis reaches `Completed` (not `Failed`) and reports partial completion — proves the `AnyCompleted` merge strategy (rule 5).
- [X] T060 [US5] **Remove the throwaway specialist, its enum member, and its provisioner branch entry.** Do not leave it in the codebase.

**Checkpoint**: The five deferred specialists are now cheap to add.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T061 [P] Run `dotnet format` and fix violations. Note: this repo produces local `ENDOFLINE` false positives — verify against CI behavior rather than chasing them. Ensure `using System.*` directives come first.
- [ ] T062 [P] Frontend checks from `src/AskLucy.Web/ClientApp`: `npx tsc -b --noEmit` (bare `tsc --noEmit` is a silent no-op here — project references), `npm run lint`, and the **full** `npm test` suite (page-level tests carry their own assertions).
- [ ] T063 Review every new `catch` block for constitution §2 VIII compliance: nothing swallowed, every failure logged with structured context, no async call left unawaited or uncaught on the client.
- [ ] T064 Confirm the new endpoint appears correctly in the OpenAPI document with accurate schemas and documented status codes (constitution §6).
- [ ] T065 Run the full backend suite with `PERSISTENCE_TESTS_CONNECTION_STRING` set. Only set `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` if you intend persistence tests to run — they are destructive against the dev database.
- [ ] T066 Walk every scenario in [quickstart.md](./quickstart.md) end to end against a running app and confirm each expected outcome.
- [ ] T067 Update `docs/ARCHITECTURE.md` (or the equivalent architecture doc) with the Site Analysis coordination chain — documentation is part of the implementation (constitution §13, §16).

---

## Dependencies

```
Phase 1 (Setup)
   ↓
Phase 2 (Foundational) ← BLOCKS EVERYTHING
   ↓
Phase 3 (US1 — MVP) ─────────┐
   ↓                          │
Phase 4 (US2) ────────────────┤ US2/US3 both build on US1's
   ↓                          │ relay + composer
Phase 5 (US3) ────────────────┘
   ↓
Phase 6 (US4) ← needs US1's relay to exist
   ↓
Phase 7 (US5) ← needs US4's tolerant-merge behavior to verify Scenario 9
   ↓
Phase 8 (Polish)
```

**Story independence**: US2, US3, and US4 each extend US1 rather than standing alone — US1 is the delivery
chain everything else hangs from. US5 is a verification story and requires US1 and US4.

**Within Phase 2**: T003→T004→T005 are sequential (same aggregate). T006, T007, T008, T014, T015 are parallel.
T013 (migration) requires T003–T009 complete.

**Within Phase 3**: T021, T022, T033 are parallel. T023→T024 sequential. T026 blocks T027. T029 blocks T030.
T036–T039 are parallel once their subjects exist.

## Parallel execution examples

```
# Phase 2, after T005:
T006 (Workflow fields)  ‖  T007 (EF configs)  ‖  T014 (DTOs)  ‖  T015 (notifier interface)

# Phase 3, at the start:
T021 (metadata record)  ‖  T022 (content composer)  ‖  T033 (client types)

# Phase 3, tests:
T036  ‖  T037  ‖  T038  ‖  T039
```

## Implementation strategy

**MVP = Phase 1 + Phase 2 + Phase 3.** That delivers the complete coordination chain and one working
specialist — a user can ask for a site analysis and receive a schematic map through the parent relay. It is
demonstrable and shippable on its own.

**Then, in order**: US2 makes findings trustworthy, US3 makes them durable, US4 makes failures safe, US5 proves
the next five specialists are cheap.

**Do not start the five deferred specialists in this feature.** They are separate specs, unblocked by T060.
