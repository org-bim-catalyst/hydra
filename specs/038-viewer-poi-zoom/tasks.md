# Tasks: POI Viewer Zoom & Focus

**Input**: Design documents from `/specs/038-viewer-poi-zoom/`

**Prerequisites**: [plan.md](plan.md) · [spec.md](spec.md) · [data-model.md](data-model.md) · [contracts/sse-events.md](contracts/sse-events.md) · [research.md](research.md) · [quickstart.md](quickstart.md)

**Organization**: Tasks grouped by user story — each story is a complete, independently testable increment.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New value objects and records that all three user stories depend on.

- [X] T001 Create `ViewportBounds` record in `src/AskLucy.Application/Locations/ViewportBounds.cs`
- [X] T002 Create `ViewerZoomCommand` record in `src/AskLucy.Application/Locations/ViewerZoomCommand.cs`
- [X] T003 [P] Add `ViewportBounds` TypeScript interface to `src/AskLucy.Web/ClientApp/src/features/viewer/types/ViewportBounds.ts`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Backend and frontend infrastructure that MUST be in place before any user story can be completed end-to-end.

**⚠️ CRITICAL**: All user story phases depend on these tasks.

- [X] T004 Extend `GeocodingCandidate` with `LocationType?` and `Viewport?` in `src/AskLucy.Application/Locations/IGeocodingProvider.cs`
- [X] T005 Extend `ConfirmedLocationData` with `LocationType?` and `Viewport?` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`
- [X] T006 Extend `ChatStreamChunk` with `ViewerZoom?` (`ViewerZoomCommand?`) in `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`
- [X] T007 Extend `activeLocationStore` with `viewport` and `locationType` fields and update `setFromAgent` signature in `src/AskLucy.Web/ClientApp/src/store/activeLocationStore.ts`

**Checkpoint**: Foundational types and store shape ready — user stories can now be implemented.

---

## Phase 3: User Story 1 — Automatic POI Zoom on Location Resolve (Priority: P1) 🎯 MVP

**Goal**: When a location is resolved, the viewer zooms to an altitude that makes the place clearly visible based on the geocoding bounding box (or location_type fallback), and a POI marker is placed at the resolved coordinates.

**Independent Test**: Send "Show me Dubai Mall" in chat. Without any follow-up, the viewer re-centres AND zooms to street/block level so the mall footprint is clearly visible. A pulsing ring marker appears labelled "Dubai Mall".

### Implementation for User Story 1

- [X] T008 [P] [US1] Extend `GoogleMapsGeocodingProvider` to parse `geometry.viewport` (NE/SW lat/lng) and `geometry.location_type` into `GeocodingCandidate` in `src/AskLucy.Infrastructure/Geocoding/GoogleMapsGeocodingProvider.cs`
- [X] T009 [P] [US1] Add tests for viewport and locationType parsing in `tests/AskLucy.Infrastructure.Tests/Geocoding/GoogleMapsGeocodingProviderTests.cs`
- [X] T010 [US1] Update `LocationResolutionService` to set `ConfirmedLocationData.LocationName = query` (user's extracted query, not `candidate.LocationName`) and pass `Viewport` and `LocationType` through in `src/AskLucy.Application/Locations/LocationResolutionService.cs`
- [X] T011 [US1] Update `AiController` to emit `__LOCATION__{json}` with the extended `ConfirmedLocationData` (new fields serialize automatically via `System.Text.Json`) in `src/AskLucy.Web/Controllers/AiController.cs`
- [X] T012 [US1] Update `aiApi.ts` to parse `viewport` and `locationType` from `__LOCATION__` JSON and pass them to `activeLocationStore.setFromAgent` in `src/AskLucy.Web/ClientApp/src/api/aiApi.ts`
- [X] T013 [US1] Add `fitBounds(ne, sw)` and `zoomToAltitude(altitudeMetres)` methods to the viewer engine in `src/AskLucy.Web/ClientApp/src/features/viewer/engine/viewerEngine.ts`
- [X] T014 [US1] Update `ViewerSurface.tsx` to call `fitBounds` when `viewport` is present, `zoomToAltitude` when only `locationType` is present (using fallback altitude table), and keep existing `zoomToLocation(lat, lng, 15)` when both are absent — in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`
- [X] T015 [P] [US1] Create `useMarkerStyleStore` Zustand store with `localStorage` persistence in `src/AskLucy.Web/ClientApp/src/store/markerStyleStore.ts`
- [X] T016 [US1] Create `POIMarkerOverlay` component using `google.maps.WebglOverlayView` + Three.js (pulsing ring geometry as default style) in `src/AskLucy.Web/ClientApp/src/features/viewer/components/POIMarkerOverlay.tsx`
- [X] T017 [US1] Mount `POIMarkerOverlay` inside `ViewerSurface` so it renders whenever `activeLocationStore` has a confirmed location in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`
- [X] T018 [US1] Run `dotnet format --include src/AskLucy.Application/Locations/ViewportBounds.cs src/AskLucy.Application/Locations/ViewerZoomCommand.cs src/AskLucy.Infrastructure/Geocoding/GoogleMapsGeocodingProvider.cs src/AskLucy.Application/Locations/LocationResolutionService.cs` to enforce CRLF and style
- [X] T019 [US1] Run `npx tsc -b --noEmit` in `src/AskLucy.Web/ClientApp` and fix any type errors from new fields

**Checkpoint**: US1 complete. Ask "Show me Dubai Mall" → viewer zooms to street level, pulsing ring marker appears labelled "Dubai Mall".

---

## Phase 4: User Story 2 — Explicit Zoom / Focus Commands (Priority: P2)

**Goal**: "Zoom in", "pull back", and natural-language equivalents are detected, the viewer camera moves accordingly, and Lucy confirms the action — never saying she cannot interact with the map.

**Independent Test**: After navigating to any location, send "zoom in". Viewer animates to a closer altitude. Lucy's reply is a short confirmation, contains no reference to Google Maps or third-party services.

### Implementation for User Story 2

- [X] T020 [P] [US2] Create `IViewerZoomDetector` interface and `ViewerZoomDetector` class with keyword detection in `src/AskLucy.Application/Locations/ViewerZoomDetector.cs`
- [X] T021 [P] [US2] Write unit tests for `ViewerZoomDetector` covering both directions, case-insensitivity, and null return when no keyword matches in `tests/AskLucy.Application.Tests/Locations/ViewerZoomDetectorTests.cs`
- [X] T022 [US2] Register `IViewerZoomDetector` → `ViewerZoomDetector` as `Transient` in `src/AskLucy.Application/DependencyInjection.cs` (inside `AddApplication()` — the class has no Infrastructure deps and belongs in the Application registration, not Infrastructure)
- [X] T023 [US2] Inject `IViewerZoomDetector` into `SendChatMessageCommandHandler` and run `Detect(request.Content)` concurrently with the existing location resolution task; set `ChatStreamChunk.ViewerZoom` only when the location resolution task also resolved a confirmed location (i.e. `confirmedLocation != null`) — when there is no active resolved location, leave `ViewerZoom = null` so the frontend does not zoom while Lucy's reply says there is nothing to zoom to — in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`
- [X] T024 [US2] Extend `AiController` to emit `__ZOOM__{direction}` when the final `ChatStreamChunk.ViewerZoom` is non-null, in the same trailing-event block as `__LOCATION__` in `src/AskLucy.Web/Controllers/AiController.cs`
- [X] T025 [US2] Extend `aiApi.ts` SSE parser to detect `__ZOOM__in` / `__ZOOM__out` lines and invoke a `onZoomCommand(direction)` callback in `src/AskLucy.Web/ClientApp/src/api/aiApi.ts`
- [X] T026 [US2] Add `zoomBy(direction: 'in' | 'out')` method to the viewer engine with factor-of-2 altitude change clamped to [50, 500_000] metres in `src/AskLucy.Web/ClientApp/src/features/viewer/engine/viewerEngine.ts`
- [X] T027 [US2] Wire `onZoomCommand` in `useChatStream.ts` (or equivalent streaming hook) to call `viewerEngine.zoomBy(direction)` only when `activeLocationStore.latitude !== null` (active location exists); when no location is active, skip the `zoomBy` call — Lucy's text reply (shaped by T028 system prompt) already handles that case — in `src/AskLucy.Web/ClientApp/src/hooks/useChatStream.ts`
- [X] T028 [US2] Update Lucy's system prompt section (or viewer-control instructions) to include explicit instructions that zoom commands ("zoom in", "zoom out", and equivalents) must be confirmed concisely — never disclaimed — in `src/AskLucy.Infrastructure/Ai/SystemPromptBuilder.cs` (or equivalent prompt file)
- [X] T029 [US2] Run `dotnet format` on new/modified C# files in this phase

**Checkpoint**: US2 complete. Send "zoom in" after navigating to a location → viewer zooms in, Lucy confirms in one sentence.

---

## Phase 5: User Story 3 — Accurate POI Display Name (Priority: P3)

**Goal**: The viewer label and Lucy's confirmation show the user's original query (e.g. "Dubai Mall"), not the geocoding formatted address (e.g. "Burj Khalifa - Downtown Dubai").

**Independent Test**: Send "Take me to Dubai Mall". The viewer label and Lucy's text both read "Dubai Mall".

### Implementation for User Story 3

- [X] T030 [US3] Verify `LocationResolutionService` (T010) correctly passes the extracted `query` string as `LocationName` — no additional code if T010 is complete; otherwise fix the assignment in `src/AskLucy.Application/Locations/LocationResolutionService.cs`
- [X] T031 [US3] Verify `POIMarkerOverlay` label (T016) uses `activeLocationStore.locationName` (which now equals the query) — if label is hardcoded or uses a different source, update it in `src/AskLucy.Web/ClientApp/src/features/viewer/components/POIMarkerOverlay.tsx`
- [X] T032 [US3] Handle the ambiguous-resolution case: when `LocationResolutionService` returns `Ambiguous` status, the system-prompt / chat reply should state what was resolved rather than silently substituting — add this to the AI instruction or handler response path in `src/AskLucy.Application/Ai/Commands/SendChatMessage/SendChatMessageCommandHandler.cs`

**Checkpoint**: US3 complete. Test with 5 Dubai landmark names — each label matches the user's query in every case.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Marker style selector, edge case guards, and final validation.

- [X] T033 [P] Create `MarkerStyleSelector` component with all four styles (pulsing ring, classic pin, 3D extruded highlight, simple dot) rendered as Three.js scene graphs inside `POIMarkerOverlay` in `src/AskLucy.Web/ClientApp/src/features/viewer/components/MarkerStyleSelector.tsx`
- [X] T034 [P] Add `MarkerStyleSelector` to the viewer control panel alongside the existing rotation toggle in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerControlPanel.tsx`
- [X] T035 Connect `MarkerStyleSelector` selection to `useMarkerStyleStore`; `POIMarkerOverlay` reads `markerStyle` from the store and re-renders on change in `src/AskLucy.Web/ClientApp/src/features/viewer/components/POIMarkerOverlay.tsx`
- [X] T036 [P] Guard degenerate bounding box (NE == SW) in `fitBounds`: fall back to `zoomToAltitude(200)` in `src/AskLucy.Web/ClientApp/src/features/viewer/engine/viewerEngine.ts`
- [X] T037 [P] Guard `localStorage` access in `markerStyleStore` with try/catch and default to `'pulsing-ring'` (verify it is there from T015; add if missing) in `src/AskLucy.Web/ClientApp/src/store/markerStyleStore.ts`
- [X] T038 [P] Guard `fitBounds` / `zoomBy` with map-ready check and log a warning (not throw) when the map is not yet initialized in `src/AskLucy.Web/ClientApp/src/features/viewer/engine/viewerEngine.ts`
- [X] T039 Ensure marker is removed/replaced on new `__LOCATION__` event: `POIMarkerOverlay` calls `setMap(null)` on the previous overlay before mounting a new one in `src/AskLucy.Web/ClientApp/src/features/viewer/components/POIMarkerOverlay.tsx`
- [X] T040 Run the full quickstart.md scenario checklist manually (5 scenarios + edge case checklist) — see [quickstart.md](quickstart.md)
- [X] T041 [P] Run `dotnet test tests/AskLucy.Infrastructure.Tests --filter "Geocoding"` and confirm all 15+ geocoding tests pass
- [X] T042 [P] Run `dotnet test tests/AskLucy.Application.Tests --filter "ViewerZoomDetector"` and confirm all zoom detection tests pass
- [X] T043 Run `npx tsc -b --noEmit` in `src/AskLucy.Web/ClientApp` and confirm zero errors
- [X] T044 Add in-flight animation guard to `zoomBy`: if a camera animation is already running (track with a `_isAnimating` flag or a stored `AbortController`-equivalent), cancel the current animation before starting the new one — prevents visual glitches from rapid successive zoom commands (FR-009) — in `src/AskLucy.Web/ClientApp/src/features/viewer/engine/viewerEngine.ts`
- [X] T045 Accessibility pass on new viewer UI (constitution §16 Gate 4): add `aria-label` and keyboard focus handling to `MarkerStyleSelector` (each style option must be keyboard-selectable and screen-reader-labelled); add `aria-label` to the floating POI text label in `POIMarkerOverlay`; verify both components pass automated a11y check (`axe` or equivalent) — in `src/AskLucy.Web/ClientApp/src/features/viewer/components/MarkerStyleSelector.tsx` and `POIMarkerOverlay.tsx`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1. Blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2. No dependency on US2/US3.
- **Phase 4 (US2)**: Depends on Phase 2. Independent of US1 (but benefits from having a resolved location for manual testing).
- **Phase 5 (US3)**: Largely covered by T010 in Phase 3. Verify-only if T010 is correct.
- **Phase 6 (Polish)**: Depends on Phase 3 (marker) and Phase 4 (zoom). T033–T035 require `POIMarkerOverlay` from T016. T044 requires `zoomBy` from T026. T045 requires `MarkerStyleSelector` from T033 and `POIMarkerOverlay` from T016.

### Within Each User Story

- Backend model changes (T004–T006) before service layer changes (T010, T023).
- `GoogleMapsGeocodingProvider` change (T008) before `LocationResolutionService` (T010).
- `fitBounds` on viewer engine (T013) before `ViewerSurface` wiring (T014).
- `POIMarkerOverlay` created (T016) before mounted in `ViewerSurface` (T017).
- `ViewerZoomDetector` created (T020) before registered (T022) before injected into handler (T023).
- `zoomBy` on engine (T026) before wired in streaming hook (T027).

---

## Parallel Example: User Story 1

```
Run together (different files, no dependency):
  T008 — GoogleMapsGeocodingProvider (backend geocoding)
  T009 — GoogleMapsGeocodingProviderTests (backend tests)
  T015 — markerStyleStore (frontend store)

Then run sequentially:
  T010 → T011 → T012 → T013 → T014 → T016 → T017
```

---

## Implementation Strategy

### MVP (User Story 1 Only)

1. Phase 1 (T001–T003): new value objects
2. Phase 2 (T004–T007): extend types + store
3. Phase 3 (T008–T019): geocoding viewport, fitBounds, POI marker
4. **Validate**: "Show me Dubai Mall" → zooms in + pulsing ring marker appears
5. Stop here for a usable MVP

### Incremental Delivery

- After Phase 3: US1 live → location queries are satisfying
- After Phase 4: US2 live → zoom commands work
- After Phase 5: US3 live → display names are accurate
- After Phase 6: all marker styles available, edge cases guarded

---

## Notes

- `[P]` = different files, no incomplete-task dependencies — safe to run in parallel
- `[US1/US2/US3]` = traceability to the spec user story
- Run `dotnet format` after every new C# file (CRLF line endings, unaligned switch arms)
- Run `npx tsc -b --noEmit` after every frontend change
- Commit after each phase checkpoint to keep git history navigable
- `appsettings.Production.json` is gitignored — never commit it
