# Phase 0 Research: AI-to-UI Floating Panel Framework

**Feature**: [../spec.md](./spec.md) | **Plan**: [../plan.md](./plan.md)

All items below were either resolved during `/speckit-clarify` (see spec.md Clarifications) or required a codebase-grounded technical decision during planning. No `NEEDS CLARIFICATION` markers remain in the Technical Context.

## Decision 1 — Panel type extensibility: registry of developer-authored renderers

**Decision**: A new, framework-agnostic `PanelTypeRegistry` (`ClientApp/src/viewer/panels/registry.ts`) maps a `typeKey` string to a `PanelTypeDefinition` (renderer component + zod schema + optional default size/resizable flag). An AI panel request references an existing `typeKey`; the registry resolves it to a renderer at render time. Introducing a new visual category (e.g., "urban-metrics") means adding one new `PanelTypeDefinition` and calling `registerPanelType()` once — no change to `floatingPanelStore`, `FloatingPanel` chrome, or the viewer.

**Rationale**: Matches the spec's clarified answer (Clarifications Q1) and mirrors the id/kind/metadata registry style already established by `viewer/api/layers.ts`'s `RenderLayer` model (`addLayer`/`removeLayer` on a typed array in `viewerEngineStore`) — the codebase already has this exact shape for a different concern (map layers), so panels reuse a proven pattern rather than inventing a new one.

**Alternatives considered**: Fully dynamic AI-defined UI schema (generic primitive renderers interpreting an arbitrary layout schema at request time) — rejected per the clarification: materially larger and riskier (schema validation surface, generic layout engine) for a first version, with no existing precedent in this codebase to build on.

## Decision 2 — Panel request transport: dedicated SignalR hub, not the chat SSE stream

**Decision**: A new hub, `PanelHub` (`src/AskLucy.Infrastructure/Panels/PanelHub.cs`), mirroring `AgentExecutionHub`/`MemoryHub`/`DocumentProcessingHub` exactly: `[Authorize]`, one group per caller keyed by `ClaimTypes.NameIdentifier` (`user:{userId}`), route `/hubs/panels`. Server code (chat response post-processing, agent tool execution) calls `IPanelNotifier.PanelRequested(userId, payload)` → `PanelNotifier` (wraps `IHubContext<PanelHub>`) → client's `useFloatingPanelHub.ts` receives it and calls `floatingPanelStore.openPanel(payload)`.

**Rationale**: The chat reply channel is plain-text SSE (`ChatsController.cs`, `data: {ContentDelta}`) with no structured-payload channel today, and panels must also be triggerable from agent tool execution (spec: "Ask Lucy and other AI agents"), not only chat replies — SSE is chat-response-scoped and wouldn't cover the agent-tool-call path. `AgentExecutionHub`'s per-user-group SignalR pattern already solves "push a structured, authorized, per-user event to the browser" and is the established precedent for exactly this shape of problem.

**Distinction from `AgentExecutionHub`'s `ToolCallCompleted` payload**: `AgentExecutionHub` deliberately excludes raw tool input/output from its events (spec 020 FR-035) because that event is a progress *summary*, not a UI payload. `PanelHub` is different in kind: the panel's data **is** the product being delivered to the user (spec FR-001), so `PanelRequested` intentionally carries the full structured panel data. This is a new, purpose-built payload type, not a loosening of FR-035's rule for agent execution telemetry.

**Alternatives considered**: Extending the chat SSE stream with a second structured event type interleaved with text deltas — rejected because it would require every chat consumer (`useChatStream.ts`) to branch on event shape and doesn't cover panel requests originating outside a chat reply (agent tool calls, spec's "other AI agents" reuse requirement).

## Decision 3 — Drag & resize: adopt `react-rnd`

**Decision**: Add `react-rnd` as a new frontend dependency; `FloatingPanel.tsx` (the new spec-028 component, distinct from the existing `components/workspace-shell/FloatingPanel.tsx` — see Decision 5 for naming) wraps its chrome in `<Rnd>`, using `disableDragging`/`enableResizing` props driven by the resolved `PanelTypeDefinition.resizable` flag (FR-005).

**Rationale**: No existing dependency in `ClientApp/package.json` covers floating-window drag+resize. `@dnd-kit/core`/`@dnd-kit/sortable` (already installed) are used exclusively for sortable-tree drag-and-drop (`FolderTree.tsx`, `KnowledgeBaseFolderTree.tsx`) and have no built-in resize primitive. `react-rnd` is small, MIT-licensed, actively maintained, and covers both drag and resize in one component/event model, avoiding the integration surface of wiring two separate libraries together.

**Alternatives considered**: (a) Hand-rolled pointer-event drag + resize-handle math — rejected, reinvents solved problems (edge/corner hit-testing, touch/pointer unification, min/max clamping) for no benefit given YAGNI/simplicity-first (constitution §2.III) favors a proven, small dependency over bespoke low-level input code. (b) `@dnd-kit` (drag) + `re-resizable` (resize) combined — rejected, two libraries with two different event models for one coherent interaction is more complexity than one purpose-built library.

## Decision 4 — Runtime panel-data validation: `zod`

**Decision**: Add `zod` as a new frontend dependency. Each `PanelTypeDefinition` carries a `zod` schema; `floatingPanelStore.openPanel()` validates the incoming payload's `data` against it before storing the panel, and a payload that fails validation is stored as an `error`-status panel (FR-017) rendering a fallback message instead of being handed to the type's renderer.

**Rationale**: `zod` is already named in this repo's own `.claude/CLAUDE.md` frontend stack, so this is completing an already-declared dependency, not introducing an unplanned one — it is simply not yet used anywhere in `ClientApp` (confirmed: no `zod`/`@hookform/resolvers` in `package.json`). Using `z.infer<typeof schema>` for each panel type's data TypeScript type means the runtime guard and the compile-time type are generated from one definition, satisfying DRY (constitution §2.III) — a hand-written type guard per panel type would need to be kept in sync with its renderer's prop types by hand.

**Alternatives considered**: Manual `typeof`/shape type-guard functions per panel type — rejected as boilerplate that drifts from the TS type it's meant to guard, with no compile-time link between the two.

## Decision 5 — Client-side panel state: new `floatingPanelStore`, new `FloatingPanel` component

**Decision**: A new Zustand store, `floatingPanelStore.ts` (`ClientApp/src/viewer/panels/store/`), session-scoped (no `persist` middleware — matches `workspaceOverlayStore`'s convention, per spec Assumption "panel layout state ... scoped to the current viewer session"). It owns: the open-panel array (id, typeKey, title, validated data, position, size, minimized flag, focus/z-order, viewer context association, validation status), the cascade-placement counter (FR-021), and max-panel-count LRU eviction (FR-022, evicts the least-recently-focused panel). A new component, `ClientApp/src/viewer/panels/components/FloatingPanel.tsx`, renders one entry from the store.

**Naming note**: `components/workspace-shell/FloatingPanel.tsx` (spec 024) already exists and is unrelated — a single, fixed-size, single-instance MUI slide-in panel for workspace controls (drag/resize/multi-instance/z-index are explicitly out of scope there; `workspaceOverlayStore.expandedControlId` only tracks one open control). It is a useful reference for MUI chrome/close-button/focus conventions but is not extended or reused for spec 028 — reusing its store/component would conflate two different concerns (a single workspace-control drawer vs. an unbounded set of independently addressable AI-generated panels). The new component is namespaced under `viewer/panels/` specifically to avoid the name collision and the conceptual conflation.

**Rationale**: Panels are viewer-scoped infrastructure (like the render-target/layer state in `viewer/`), not a page-level feature concern, so they belong in the framework-agnostic `viewer/` package (mirroring spec 027's Decision to keep the engine's own state — `viewerEngineStore` — separate from `features/viewer/`'s page wiring), with a thin `features/viewer/` wiring layer mounting the panel host inside `ViewerSurface.tsx`.

## Decision 6 — Opacity preference: new `UserPanelPreference` aggregate + new Settings tab

**Decision**: A new small backend aggregate, `UserPanelPreference` (`Domain.Panels`), following `UserVoicePreference`'s exact shape: `OpacityPercent` (int, clamped 40–100 per spec Clarifications Q4) with `Create`/`SetOpacityPercent`, `ModifiedAtUtc/ModifiedBy`. CQRS: `GetUserPanelPreferenceQuery`/`SaveUserPanelPreferenceCommand` (+ validator, + DTO) under `Application/Panels`, EF Core configuration + migration under `Infrastructure/Persistence`, `PanelsController` exposing `GET/PUT /api/v1/panels/preferences`. Frontend: `panelPreferencesStore.ts` (Zustand + `persist`, mirroring `voicePreferencesStore.ts` exactly — instant localStorage-cached restore, `hydrateFromServer()`, optimistic `update(patch)`, `error` field for Snackbar surfacing). Settings UI: a new tab, `Viewer` (appended to `SETTINGS_TAB_INDEX` as index `8`, not inserted mid-sequence, to avoid renumbering every other tab's existing references/tests), hosting the opacity slider.

**Rationale**: `UserVoicePreference` is the closest, most recently added precedent for "one small per-user preference entity, one CQRS pair, one settings-tab control" and this feature's opacity setting is structurally identical (one bounded numeric field). A new tab (rather than embedding into `ChatConfigurationTab`) is required because `ChatConfigurationTab` explicitly documents itself as "a hub, not an embedded-controls tab" that links out to other tabs rather than hosting unrelated controls inline (spec 025 FR-012) — adding an unrelated opacity slider there would violate that already-established, in-repo convention. Panel opacity belongs to the viewer/panel domain, not chat configuration.

**Alternatives considered**: Folding `OpacityPercent` onto the existing `UserVoicePreference` entity — rejected, violates SRP (constitution §2.II): voice persona and panel transparency are unrelated concerns that would only share a table by coincidence of both being "a small user preference," and a future change to one's validation/versioning would risk the other.

## Decision 7 — Viewer context association & bidirectional communication

**Decision**: Reuse the existing `ViewerEventBus` (`viewer/engine/viewerEventBus.ts`) and `ViewerEngine` command surface (`select`, `clearSelection`, `createOverlay`) rather than building a second event system. A panel's `ViewerContextAssociation` stores a reference (element/layer id) in the same id-space `viewerEngineStore`'s layers already use. Panel → viewer: an in-panel interaction calls `viewerEngine.select(id)` / `viewerEngine.zoomToLocation(...)` directly (FR-014, US4 AS1). Viewer → panel: `floatingPanelStore` subscribes to `ViewerEventBus` selection/removal events and marks an associated panel's `contextStatus` as `stale` or `invalid` (FR-014, US4 AS2; Edge Cases: viewer object removed) rather than polling.

**Rationale**: `ViewerEngine`/`ViewerEventBus` already exist specifically as the "documented, typed command/event API" spec 027 built for future features (including AI agent integration) to call — building a second, panel-specific event mechanism would duplicate infrastructure the constitution's DRY principle (§2.III) forbids duplicating.

## Summary of new dependencies

| Package | Scope | Why not already covered |
|---|---|---|
| `react-rnd` | Frontend | No existing drag+resize-for-floating-windows library (Decision 3) |
| `zod` | Frontend | Declared in CLAUDE.md's stack but not yet installed/used anywhere in `ClientApp` (Decision 4) |

No new backend NuGet packages are required — `UserPanelPreference` and `PanelHub` both follow existing MediatR/EF Core/SignalR patterns already registered in `Infrastructure/DependencyInjection.cs`.
