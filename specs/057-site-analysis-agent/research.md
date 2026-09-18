# Research: Site Analysis Agent (SPEC-057)

**Date**: 2026-09-17
**Prerequisites**: specs 020 (agent framework), 021 (MCP), 022 (workflow orchestration), 042 (site boundary
resolution), 045 (conversational agent runtime), 049 (panel content model), 052 (solar analysis), 054 (panel
placement/reopen)

Every decision below was verified against the current code, not assumed from the specs that introduced it.
Line references are to the state of the repository on the date above.

---

## D1 — Fan-out mechanism: the Workflow engine's Parallel/Merge, not a new Agent parent-child schema

**Decision**: Model the dispatch as a system-owned `WorkflowVersion` shaped
`Start → Parallel → [one node per specialist] → Merge → End`, executed by the existing
`WorkflowExecutionOrchestrator`.

**Rationale**: `ExecuteParallelAsync`
(`src/AskLucy.Application/Workflows/Runtime/WorkflowExecutionOrchestrator.cs:847-995`) already fans branches out
concurrently via `Task.WhenAll` (line 944), gated by `WorkflowBudgetGuard.ResolveMaxParallelNodes` (line 895),
with per-branch isolated value snapshots (lines 908-913). Adding `ParentExecutionId` to `AgentExecution` would
mean a schema migration, new cascade-cancel semantics, and a second fan-out runner — duplicating working code.

**Verified constraint**: branches must be **exactly one node long** and must all converge on the **same** Merge
node (lines 870-887). Each specialist is one tool call, so this is satisfied; it is recorded here because it
permanently constrains how specialists are added (FR-031).

**Alternatives considered**:
- *Agent parent/child executions* — rejected; duplicates the fan-out runner (above).
- *Sequential specialists in one agent's step plan* — rejected; serializes independent work and defeats
  first-done-first-delivered (FR-004/FR-005).

---

## D2 — Specialists are `IAgentTool` classes behind `NativeTool` nodes

**Decision**: Each specialist is a DI-registered `IAgentTool`, invoked by a `NativeTool` workflow node.

**Rationale**: `NativeToolNodeExecutor`
(`src/AskLucy.Application/Workflows/Runtime/NativeToolNodeExecutor.cs:16-55`) already adapts any registered
`IAgentTool` into a workflow node, resolving its input through the workflow expression evaluator.
`AgentToolExecutionContext` carries `UserId` (`src/AskLucy.Application/Agents/Tools/IAgentTool.cs:37`), which the
relay needs.

**`AiAgent` nodes were evaluated and rejected — two verified blockers**:
1. `AgentNodeExecutor` resolves its agent with `GetByIdForOwnerAsync(agentId, context.UserId)`
   (`AgentNodeExecutor.cs:61`) — a **system-owned agent cannot be found** this way by an ordinary user.
2. It creates nested executions with `userChatId: null` (`AgentNodeExecutor.cs:94`), discarding the chat link the
   relay needs to post its notice.

**Consequence**: the relay resolves the owning chat from the persisted `SiteAnalysis` record via a
`siteAnalysisId` passed in tool input, never from `AgentToolExecutionContext.UserChatId`.

**Alternatives considered**:
- *Five separate `Agent` entities* — rejected for this release; each needs its own version/publish lifecycle for
  what is currently fixed platform logic (YAGNI, constitution §2 III). Revisit when prompts become editable.

---

## D3 — Incremental delivery: the specialist calls the relay inline; workflow node events are unusable for this

**Decision**: Each specialist calls `ISiteAnalysisResultRelay.ReportAsync(...)` as the last step of its own
`ExecuteAsync`, before returning its `AgentToolResult`.

**Rationale — this is the single most important finding of this research.** `ExecuteParallelAsync` **batches**
its node notifications: it accumulates `NodeStarted`/`NodeCompleted` into a `pendingNotifications` list and
flushes them only after **every** branch has settled
(`WorkflowExecutionOrchestrator.cs:948-973`, whose own comment reads "the live push waits until every branch's
row is …"). Building delivery on `IWorkflowExecutionNotifier` would therefore deliver all findings in one batch
at the end — precisely the behavior FR-005 forbids.

An inline call inside the tool fires the moment that branch's work finishes, independent of its siblings.

**Alternatives considered**:
- *New `NotifySubAgentCompletedAsync` on `IWorkflowExecutionNotifier`* — rejected; it would have to be invoked
  from inside the batching loop anyway, so it adds an interface member without solving the ordering problem.
- *Polling from the client* — rejected; the platform already has a push mechanism, and polling cannot
  distinguish "not ready" from "failed".

---

## D4 — The parent is a validation gate, not a pass-through

**Decision**: `ISiteAnalysisResultRelay` validates every reported result before anything reaches the user:
required metadata present, narrative non-empty, content blocks conform to the server-side block vocabulary, and
the image specialist carries a resolved `DocumentId`. Invalid results are persisted as `Rejected` and never
delivered.

**Rationale**: FR-006/FR-007 require the coordinating agent to be the only component that can cause a delivery,
and specialist output is partly model-generated. Validating at the parent keeps that check in exactly one place
rather than trusting each specialist. Mirrors how `PresentPanelContentCapability`
(`src/AskLucy.Application/Conversations/Capabilities/PresentPanelContentCapability.cs`) validates composed
content before pushing it.

---

## D5 — Chat notices need a new notifier; there is no chat hub

**Decision**: Add `ISiteAnalysisNotifier` (Application) + `SiteAnalysisHub` (Infrastructure), mirroring
`IPanelNotifier`/`PanelHub`.

**Rationale**: Verified that chat delivers over SSE per turn, and the only SignalR hubs that exist are
`AgentExecutionHub`, `DocumentProcessingHub`, `MemoryHub`, `PanelHub`, `RetrievalIndexingHub`, and
`WorkflowExecutionHub` — none carries chat messages. A background job therefore has no existing way to make a
message appear in an open conversation.

`IPanelNotifier` is the shape to copy: one method, keyed by `userId`, Application never references SignalR
(`src/AskLucy.Application/Abstractions/IPanelNotifier.cs:14-17`, constitution §3).

**Note**: the panel itself still goes through the existing `IPanelNotifier`/`PanelHub`; only the chat notice and
the closing outcome need the new hub.

---

## D6 — Findings persist in a new aggregate

**Decision**: New `SiteAnalysis` aggregate root owning `SiteAnalysisResult` children. See `data-model.md`.

**Rationale**: FR-016/FR-017 require findings to outlive the browser session. Verified that
`floatingPanelStore` (`src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts`) is deliberately
session-scoped with no persistence and no re-sync path, and that `useFloatingPanelHub`'s own documentation states
there is no REST fallback for AI-requested panels. This feature is the first whose results arrive over minutes at
unpredictable times, so it cannot inherit that limitation.

**Alternatives considered**:
- *Persisting panel layout generally* — rejected; out of scope, and layout is genuinely ephemeral. What must
  survive is the **finding**, not where its window sat.
- *Storing findings on `UserChat` like `ActiveBoundary`* — rejected; there are many findings per analysis and
  many analyses per chat, so a single column cannot represent them.

---

## D7 — Image generation: OpenAI is the only capable provider (verified)

**Decision**: Resolve the image provider from configuration, defaulting to `openai`.

**Verified by reading all four implementations** — this closes the "unverified" flag carried in the pre-spec
plan:

| Provider | `GenerateImageAsync` |
|---|---|
| `OpenAIProvider` | Real implementation, calls `images/generations` (`OpenAIProvider.cs:166-185`) |
| `AnthropicProvider` | `throw new NotSupportedException` (`AnthropicProvider.cs:170-174`) |
| `GoogleGeminiProvider` | `throw new NotSupportedException` (`GoogleGeminiProvider.cs:141-145`) |
| `OpenRouterProvider` | `throw new NotSupportedException` (`OpenRouterProvider.cs:156-160`) |

`IAIProviderResolver.Resolve(providerKey)` takes an explicit key; there is no "find a provider that supports
images" mechanism, so the key is configuration (`SiteAnalysisOptions`), not inference. Per constitution §9 this
stays behind the provider abstraction — adding a future image-capable provider is a configuration change.

**Failure path**: catch `NotSupportedException` and the existing `AiProviderException` hierarchy and return
`AgentToolResult.Failure` (FR-030). Do not introduce new exception types.

---

## D8 — The generated image becomes a real Document via the existing finalizer

**Decision**: Reuse `DocumentUploadFinalizer.FinalizeAsync(ownerId, fileName, content, sizeBytes, actor, ct)`
(`src/AskLucy.Application/Documents/Commands/DocumentUploadFinalizer.cs:33-72`).

**Rationale**: `GenerateImageAsync` returns a **transient provider-hosted `Uri`**, which cannot be handed to the
client: `imageBlockSchema` constrains `fileId` to a platform file id and rejects external addresses outright
(`blocks.ts:80-88`), and `ImageBlockRenderer` resolves it through `/documents/{fileId}/download`
(`ImageBlock.tsx:12-17`). So the bytes must be downloaded and persisted as a Document owned by the requesting
user (FR-029).

The finalizer is the right seam because it already performs magic-byte content validation (constitution §8),
storage-quota enforcement, SHA-256 checksum de-duplication, and `Document`/`DocumentVersion`/`DocumentChecksum`
creation together. Writing a bespoke path would duplicate all four.

**Open verification item for implementation**: confirm `IDocumentFileValidator` accepts PNG — `DocumentFileType`
declares `Png`/`Jpeg` (`src/AskLucy.Domain/Documents/Document.cs:19-20`), but the validator's accepted-type set
must be checked, and extended if image uploads were previously restricted.

---

## D9 — Presentation reuses the existing content-block vocabulary

**Decision**: Compose findings from `HeadingBlock`, `TextBlock`, `KeyValueBlock` (provenance), and `ImageBlock`.
No new block kind.

**Rationale**: A new kind would require a zod schema, a renderer, a bumped `CONTENT_VOCABULARY_VERSION`, and
regenerated JSON-Schema contracts, for no expressive gain (constitution §2 III, §7 design-system rule). The
vocabulary already validates block-at-a-time so one malformed block degrades visibly without destroying its
siblings (`blocks.ts:94-110`).

---

## D10 — Confidence is rule-based and must not borrow geocoding `Importance`

**Decision**: A three-level ordered enum (`High`/`Medium`/`Low`) assigned by a documented rule per specialist.

**Rationale**: FR-013/FR-015. `GeocodingCandidate.Importance` is populated on **incompatible scales** by the two
providers — `GoogleMapsGeocodingProvider` uses a fixed lookup by `location_type` (ROOFTOP 0.90 … APPROXIMATE
0.40), while `NominatimGeocodingProvider` passes through the raw OSM importance float. Mixing them has already
caused a production defect in this codebase. Because site analysis consumes an **already-resolved** site and
never re-ranks geocoding candidates, the risk cannot enter through the front door — but any future specialist
scoring "how good is this match" must not reach for an `Importance`-shaped field by analogy.

---

## D11 — Data with no source goes behind stub provider interfaces

**Decision**: Define `IZoningDataProvider`, `IFloodDataProvider`, `IClimateDataProvider` in Application with no
production implementation, each returning an explicit `unavailable` outcome.

**Rationale**: FR-014 forbids presenting an invented figure as measured. FAR, setbacks, height limits, flood
modelling, wind, and climate are not in OpenStreetMap — the only geospatial source currently integrated
(`OverpassBoundaryCandidateProvider`). Declaring the seam now means a future release supplies an implementation
plus a DI registration with no change to calling code (constitution §3 infrastructure isolation).

The `unavailable` outcome mirrors `SiteBoundaryResolverTool`'s existing
`confirmed|no_candidates|ambiguous|unavailable` result vocabulary rather than inventing a new one.

**Note on YAGNI (constitution §2 III)**: three interfaces with no implementation is close to speculative
generality. They are justified because FR-014 is a *current* requirement — the specialists that ship later must
be able to say "unavailable" for a named reason, and that contract is what this release is committing to. No
implementation, DTO, or configuration beyond the interface and its outcome type is created.

---

## D12 — Starting a shared system workflow bypasses the ownership-guarded command

**Decision**: `ISiteAnalysisDispatcher` looks the workflow up by `SystemKey`, constructs
`WorkflowExecution.Create(workflowId, versionId, runByUserId, triggerType, triggeringEventReferenceJson,
inputsJson, actor)` (`src/AskLucy.Domain/Workflows/WorkflowExecution.cs:81-101`) directly, persists it, and calls
`IWorkflowExecutionRunner.EnqueueAsync`.

**Rationale**: `StartWorkflowExecutionCommandHandler` calls
`WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(...), userId)` — a hard
`OwnerId == userId` check that a shared system workflow can never satisfy. This is the same reason every
existing conversation capability (`OpenSolarAnalysisCapability`, `PresentPanelContentCapability`) calls services
directly rather than going through the public CQRS surface.

`RunByUserId` **must** be the real signed-in user, not a system id: both `IPanelNotifier` and the new
`ISiteAnalysisNotifier` are keyed by user id, so a system id would push findings to nobody (FR-012).

**Supporting changes**: add `IsSystemOwned`/`SystemKey`/`CreateSystemProvisioned` to `Workflow`, mirroring
`Agent.cs:102-124,338-377`; add `IWorkflowRepository.GetBySystemKeyAsync`, mirroring
`IAgentRepository.GetBySystemKeyAsync`.

**Provisioning**: `SystemWorkflowProvisioner` + `SystemWorkflowProvisioningHostedService`, mirroring
`SystemAgentProvisioner` and `SystemAgentProvisioningHostedService`
(`src/AskLucy.Infrastructure/DependencyInjection.cs:461-463`) — including its **defer-on-pending-migrations**
behavior (`SystemAgentProvisioner.cs:50`, `:64`), so a fresh or mid-migration database does not crash startup.

---

## D13 — Failure handling: capture everywhere, one closing outcome

**Decision**: Every failure is caught at the point it occurs, persisted with its reason and diagnostic context,
and written to structured logs (FR-022/FR-022a). No per-specialist failure produces a notice or panel (FR-024).
Each analysis produces exactly one closing outcome for the user (FR-025–FR-027).

**Rationale**: Constitution §2 VIII "No Silent Failures" governs **capture and diagnosability** — a failure must
never be swallowed or pass unobserved, so a malfunction can be traced to its cause afterwards. It does not
require exposing failures to end users; suppressing per-specialist noise is compliant. The closing outcome is a
**product** requirement (a user must not be left forever on a start acknowledgement), not a compliance one.

The tolerant Merge strategy `AnyCompleted` (`WorkflowExecutionOrchestrator.cs:989`) is what keeps one failed
branch from failing the whole analysis (FR-020) — it is the engine's only branch-failure resilience mechanism.

---

## Summary of what is reused versus built

**Reused unchanged**: workflow fan-out engine, Hangfire execution runner, `IAgentTool`/`NativeTool` adapter,
`IPanelNotifier`/`PanelHub`, panel content vocabulary and renderers, `floatingPanelStore`, boundary/location
resolution, `IAIProvider`/`IAIProviderResolver` and its exception hierarchy, `DocumentUploadFinalizer`, document
download and signed-URL path.

**Built new**: `SiteAnalysis` aggregate and repository, `ISiteAnalysisResultRelay` (the parent), the dispatcher,
`ISiteAnalysisNotifier` + `SiteAnalysisHub`, `RequestSiteAnalysisCapability`, the schematic-image specialist,
three stub data-provider interfaces, the system-workflow provisioner, and one client hook.

**Modified**: `Workflow` (system ownership fields), `IWorkflowRepository` (one lookup), `SubAgentArea` (one enum
value), DI registration.
