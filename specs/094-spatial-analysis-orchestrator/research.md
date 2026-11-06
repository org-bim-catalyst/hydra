# Research: Spatial Request Bundle & Analysis Orchestrator (SPEC-094)

**Date**: 2026-11-06
**Prerequisites**: specs 045 (conversational agent runtime), 057 (Site Analysis Agent), 090 (POI discovery),
092 (dataset sources), 093 (proximity analysis).

Every decision below was checked against the current code, not assumed from the spec that introduced it.
Line references are to the repository state on the date above.

---

## D1 — Dispatch in-process (`Task.WhenAll`), not through the workflow engine's `Parallel`/`Merge`

**Decision**: The coordinator is a conversation capability that dispatches independent operations concurrently
inside the current turn, then merges their results. It does **not** create a workflow execution.

**Rationale — verified blockers in the workflow engine**:

1. **Notifications are batched.** `WorkflowExecutionOrchestrator.ExecuteParallelAsync` collects
   `pendingNotifications` and flushes them only after every branch has settled
   (`src/AskLucy.Application/Workflows/Runtime/WorkflowExecutionOrchestrator.cs:1027-1061`). The engine is
   therefore incapable of delivering per-operation progress; a coordinator built on it would either batch or
   duplicate the notification path.
2. **Branches are constrained.** Each branch must be exactly one node and all branches must converge on the same
   Merge node (`WorkflowExecutionOrchestrator.cs:946-958`). That is workable for specialists, but it fixes the
   graph shape rather than allowing dependency-aware bundles.
3. **It is a background path.** `SiteAnalysisDispatcher` enqueues a Hangfire job
   (`src/AskLucy.Application/SiteAnalysis/SiteAnalysisDispatcher.cs:63`). Spatial requests must answer in the
   same conversational turn (spec Assumptions; spec 093's resolved integration decision), and the existing
   viewer contract is a mid-stream SSE trailing event, not a hub push.

**What is reused from 057 is the pattern, not the engine**: a coordinator that is the only component allowed to
deliver, specialist classes behind a narrow interface, and a single declaration of the specialist set (compare
`SiteAnalysisSpecialists.All`, `src/AskLucy.Application/SiteAnalysis/SiteAnalysisSpecialists.cs:32-39`).

**Alternatives considered**:

- *System workflow + `Parallel`/`Merge`* — rejected: results arrive after the turn, notification batching, and a
  new hub would be required.
- *Sequential specialist calls* — rejected: fails SC-003 and multiplies latency.
- *One `IAgentTool` per spatial capability, model-routed* — rejected: that is the current state this feature is
  replacing (FR-001), and it lets the decider pick a specialist directly, violating FR-009.

---

## D2 — One front door: a new capability; retire `FindPointsOfInterestCapability`

**Decision**: Add `SpatialAnalysisCapability : IConversationCapability` and remove
`FindPointsOfInterestCapability` from DI (`src/AskLucy.Application/DependencyInjection.cs:374`).

**Rationale**:

- `AgentToolCatalog` exposes every registered `IAgentTool`/`IConversationCapability`
  (`src/AskLucy.Application/Agents/Tools/AgentToolCatalog.cs`), and the decide step routes by Tier 1 text. Two
  spatial capabilities would present two competing index entries and two front doors — exactly FR-001's
  prohibition.
- Retiring rather than delegating keeps the POI path from remaining a second delivery route. The POI logic that
  matters (category resolution, keep/replace question, provider error semantics) moves into the POI specialist
  and the coordinator.
- `SubAgentArea` already carries a feature-specific value added by 057 (`SiteAnalysis`). A new `Spatial` value
  follows that precedent; it also keeps the delegated-slice scoping rule intact
  (`src/AskLucy.Application/Conversations/Capabilities/IConversationCapability.cs:28-58`).

**Alternatives considered**:

- *Extend `FindPointsOfInterestCapability` in place* — rejected: its name, schema and usage guidance are
  POI-specific; widening it would mislead the decider and leave proximity/area requests outside it.
- *Keep both and add routing guidance* — rejected: two paths, model-dependent selection, no single outcome owner.

---

## D3 — The bundle is an internal, versioned Application contract

**Decision**: `SpatialRequestBundle` is an Application-owned record serialized with System.Text.Json, carrying
`bundleVersion: 1`. It is produced by the coordinator, consumed by specialists, and never exposed as a REST
request.

**Rationale**:

- There is no REST spatial endpoint today. Spec 093's tasks record the resolved integration decision: the
  "Spatial Request Bundle" is the capability pipeline plus `ChatStreamChunk` trailing events, not a separate
  endpoint (`specs/093-proximity-analysis/tasks.md:9-13`).
- A version field is still required by FR-003 because the bundle is the extension contract: a stale or unknown
  version must be rejected rather than misinterpreted. Validation lives in `SpatialBundleValidator`
  (FluentValidation, already a dependency).
- `ChatStreamChunk` is the established carrier for viewer-facing commands
  (`src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs:45-110`).

**Extensibility rule**: adding a specialist adds a new `operationKind` and its payload, never changes the meaning
of an existing field. Unknown operation kinds fail validation with a user-visible explanation; they are not
silently dropped.

**Alternatives considered**:

- *A versioned public REST endpoint* — rejected: no client needs it, and it would create a second spatial path.
- *Untyped `JsonDocument` operations* — rejected: loses compile-time safety and validation; the bundle is the one
  place where strong typing pays.

---

## D4 — Specialist contract and registry

**Decision**:

```csharp
public interface ISpatialSpecialist
{
    string OperationKind { get; }
    Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken);
}
```

`SpatialSpecialistRegistry` is built from the injected `IEnumerable<ISpatialSpecialist>` and keyed by
`OperationKind` (case-insensitive). Specialists are registered in Application DI beside the existing capability
registration.

**Rationale**:

- One method, one operation kind — ISP and OCP at once. Adding a specialist edits no existing class (FR-006,
  FR-028).
- A **string** operation kind (with `SpatialOperationKinds` constants for the known values) rather than an enum
  means a new specialist does not require editing a shared enum file — the enum would be a hidden core change
  every time a capability is added, defeating FR-028.
- The pattern mirrors how site-analysis specialists are declared once and read by two assemblies
  (`SiteAnalysisSpecialists`, SPEC-057), while keeping retrieval in Infrastructure through Application-owned
  provider abstractions (constitution §2 I).
- `SpatialExecutionContext` carries only what a specialist may legitimately need: user id, chat id, resolved
  study area, layer state snapshot, and the map context. It never carries the capability's `TurnContext` — that
  would couple specialists to the conversation runtime.

**Alternatives considered**:

- *Specialists implement `IAgentTool`* — rejected: tools are model-callable and individually routed; specialists
  must be reachable only through the coordinator (FR-009).
- *Enum operation kinds* — rejected for the extensibility reason above.
- *A dispatch switch inside the coordinator* — rejected: that is the bespoke orchestration this feature removes.

---

## D5 — Specialist set and migration of POI

**Decision**:

- `PoiDiscoverySpecialist` wraps the existing `PoiCategoryCatalog` and `IPoiQueryProvider`
  (`src/AskLucy.Application/PointsOfInterest/PoiModels.cs:80-87`,
  `src/AskLucy.Infrastructure/Overpass/OverpassPoiQueryProvider.cs`). It performs category resolution and the
  provider query; it returns features + coverage, not a chat question and not a layer command.
- `ProximitySpecialist` is the registration seam for spec 093's calculator/provider. If 093's implementation
  lands after this feature, only its specialist registration is added; the coordinator is unchanged.
- Study-area resolution is a shared `StudyAreaResolver` used by the coordinator, not a layer-producing
  specialist: boundary resolution produces a precondition (the area), not an independent user-facing result.
  A boundary-highlight result can be added later as a specialist through the same seam.

**Rationale**: FR-029 forbids duplicating retrieval logic. `FindPointsOfInterestCapability` already owns
category resolution and the clear-or-keep UX (`FindPointsOfInterestCapability.cs:121-226`); those behaviours must
be preserved, but they belong to the coordinator because they concern *delivery*, not retrieval.

**Alternatives considered**:

- *Call the existing capability from the specialist* — rejected: a capability expects `TurnContext` and produces
  a layer command; it would keep the old delivery path alive underneath the new one.
- *Make boundary resolution a specialist that returns a layer* — deferred: it would add a layer kind with no
  current requirement; YAGNI.

---

## D6 — One normalized result and one viewer event

**Decision**: Replace `PoiLayerCommand` with `SpatialLayerCommand` (a list of layers, each with a `kind`), carry
it on `ChatStreamChunk.SpatialLayers`, and emit a single `__SPATIAL_LAYERS__` SSE event. The client parses it
into a generalized `spatialLayersStore`; the existing POI marker rendering becomes the `markers` layer kind.

**Rationale**:

- FR-011/FR-018 require one normalized shape the viewer can render for markers, buffers and area highlights.
  `PoiLayerCommand` is marker-only (`src/AskLucy.Application/Viewer/PoiLayerCommand.cs:19-25`).
- The existing trailing-event mechanism is proven: `AiController` flushes `__POI_LAYERS__` the moment the chunk
  yields (`src/AskLucy.Web/Controllers/v1/AiController.cs:323-326, 930-950`), and the client parses it in
  `aiApi.ts`. Reusing that mechanism with a wider payload is the smallest change that meets the requirement.
- A second event alongside `__POI_LAYERS__` would preserve the parallel path the feature exists to remove.

**Layer shape** (details in [contracts/spatial-layers-event.md](contracts/spatial-layers-event.md)): `layerId`,
`analysisId`, `name`, `kind` (`markers` | `buffer` | `area` | `band`), `geometry` (point/line/polygon or marker
list), `style`, `visible`, `legendLabel`, `sourceOperation`. Geometry is GeoJSON-compatible and uses WGS-84 —
the same representation the site-boundary and proximity contracts already use.

**Alternatives considered**:

- *Keep `__POI_LAYERS__` and add `__SPATIAL_LAYERS__`* — rejected: two parsers, two stores, two truths.
- *Move layer rendering into server-side panels* — rejected: the viewer already owns layer state and controls;
  the spec requires user-led show/hide/remove/clear.

---

## D7 — Map context plumbing

**Decision**: Add `MapViewportContext` (centre latitude/longitude, zoom, bounds southwest/northeast) to
`SendChatMessageCommand` and `TurnContext`. The client captures it from the live Google Maps instance at send
time. The active boundary is **not** sent from the client: the server already has it on
`TurnContext.ActiveBoundary`.

**Rationale — verified current state**:

- The only view state sent today is `ActivePoiCategories`
  (`src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommand.cs:67-72`).
- "Here" is resolved from the persisted active pin, not the camera
  (`FindPointsOfInterestCapability.cs:203-211`).
- `activeLocationStore` holds the geocoded place's `viewport`
  (`src/AskLucy.Web/ClientApp/src/store/activeLocationStore.ts:24-30`), which is the named place's extent — not
  where the user has since panned or zoomed. Using it for "this area" would answer about the wrong area.
- The map instance is already reachable through the viewer (`googleMapsStore.map`, used by `ViewerSurface`), so
  reading centre/zoom/bounds at send time needs no new integration.

**Validation**: finite coordinates, WGS-84 ranges, zoom within the supported range, ordered bounds, and a study
area within existing provider limits. Invalid context is rejected with a user-visible explanation; it is never
silently clamped and then described as the requested area.

**Alternatives considered**:

- *Reuse `ActiveLocation.Viewport`* — rejected: stale relative to the camera.
- *Use only the active boundary* — rejected: a conversation may have no boundary, or a boundary unrelated to
  where the user is looking.
- *Persist camera state server-side* — rejected: unnecessary state; the client is the source of truth per turn.

---

## D8 — Session-scoped layer state

**Decision**: Generalize `PoiLayerStateStore` into `SpatialLayerStateStore`: keyed by chat, storing layers by
`analysisId` + `layerId`, with the existing pending-question model and 10-minute expiry.

**Rationale**:

- The existing store is deliberately in-memory and per-chat
  (`src/AskLucy.Application/PointsOfInterest/PoiLayerStateStore.cs:32-47`); spec 093 reached the same decision
  for proximity layers, and the spec Assumptions exclude persistence.
- The pending question is what keeps the keep-or-replace UX from re-querying the provider
  (`FindPointsOfInterestCapability.cs:149-188`). Removing it would regress spec 090's accepted behaviour.
- Generalizing the key from `category` to `layerId` is required because one analysis now produces several
  layers and several operations can coexist.

**Alternatives considered**:

- *Keep the POI store and add a parallel spatial store* — rejected: two sources of truth for what is on the map.
- *Persist layer state* — rejected: view state of an open browser session, meaningless after a restart; would
  add a table and a migration for no user value.

---

## D9 — Failure isolation and observability

**Decision**: Every operation is wrapped. `SpatialSpecialistOutcome` records `Succeeded`, `Empty` or `Failed`
with a diagnostic reason and optional coverage. The coordinator merges successful/empty outcomes, attaches
failed-operation warnings, logs each failure with `bundleId`, `analysisId`, `operationId`, `operationKind` and
specialist type, and returns `AgentToolResult.Failure` only when every operation failed.

**Rationale**: FR-025/FR-027 and constitution §2 VIII. The current POI path already distinguishes provider
failure from empty success for exactly this reason (`FindPointsOfInterestCapability.cs:256-263`); the coordinator
generalizes that rule instead of re-learning it per specialist.

**Alternatives considered**: fail-fast — rejected (discards independent successful work); returning an empty
result on failure — explicitly forbidden by the spec and by the existing provider contract.

---

## D10 — Concurrency bounds and dependency handling

**Decision**: Independent operations run concurrently under a `SemaphoreSlim` cap (initial value 4, a named
constant). Operations declaring `dependsOn` are executed after their prerequisites resolve; the builder emits a
dependency-respecting order and the coordinator awaits prerequisites before starting dependents.

**Rationale**: SC-003 requires measurable concurrency; the free Overpass endpoints already saturate under load
(the repository's own POI history), so unbounded fan-out would trade latency for provider failure. The cap
mirrors `WorkflowBudgetGuard.ResolveMaxParallelNodes`'s role without pulling in the engine.

**Alternatives considered**: unbounded `Task.WhenAll` — rejected (provider quotas); serial execution — rejected
(fails SC-003).

---

## D11 — Untrusted specialist output

**Decision**: Before a `SpatialLayerCommand` is built, validate layer kind, geometry type and coordinates
(finite, WGS-84 ranges, ring closure for polygons), attribute allowlist, legend/name length, and absence of
external URLs. Invalid layers are dropped with a warning and logged; they never reach the viewer as raw data.

**Rationale**: Specialist output is model/provider-influenced (constitution §8, prompt-injection rule); the
site-analysis relay already validates specialist content in exactly this way (057
`contracts/result-relay.md`, validation gate). The viewer is an injection surface if it renders arbitrary
attributes or external addresses.

**Alternatives considered**: trust specialists — rejected; validate in the client — rejected (client-side checks
are not a security boundary).

---

## D12 — Testing strategy

- **Application unit tests**: bundle version rejection, validation rules, registry lookup, parallel dispatch
  (fake specialists with delays), dependency ordering, merge/coverage, failure isolation, map-context
  resolution, provider-failure ≠ empty.
- **Web/contract tests**: `SpatialAnalysisCapability` result → `ChatStreamChunk.SpatialLayers` →
  `__SPATIAL_LAYERS__` payload; controller writes and flushes it mid-stream.
- **Frontend**: `spatialLayersStore` (apply/keep/replace/remove/clear, cross-chat isolation), SSE parsing in
  `aiApi.ts` and both `useChatStream` branches, overlay/legend rendering and error path.
- **Behavioural concurrency assertion**: two fake specialists with controlled delays; total elapsed is
  materially below the sum (SC-003), without a perf harness.
- **Full-suite rule**: `ChatPage.test.tsx` holds its own assertions independent of component tests; run the full
  ClientApp suite, not only touched files (repository memory).
