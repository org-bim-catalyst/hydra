---

description: "Task list for Buildings-Only Map Style"
---

# Tasks: Buildings-Only Map Style

**Input**: Design documents from `/specs/048-buildings-only-map-style/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the constitution requires unit/integration testing, and the
existing map-style architecture already has a test file per module touched here.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent
implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

All paths are relative to `src/AskLucy.Web/ClientApp/src/` unless stated otherwise.

---

## Phase 1: Setup

- [X] T001 Confirm the existing ClientApp test suite runs clean on `main` before
      starting (`npm test` from `src/AskLucy.Web/ClientApp`) — establishes a clean
      baseline so any later failure is attributable to this feature's changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared type and rendering-logic changes every user story below builds on.

**⚠️ CRITICAL**: Complete this phase before starting any user story phase.

- [X] T002 Add `'buildings-only'` to the `MapStyleId` union in
      `viewer/api/commands.ts`
- [X] T003 [P] Add a `BUILDINGS_ONLY_STYLE: google.maps.MapTypeStyle[]` constant in
      `viewer/layers/gis/GoogleMapsGisLayer.ts` hiding `road`, `poi`, `transit`,
      `administrative`, and `landscape.natural` (`stylers: [{ visibility: 'off' }]`
      each), per research.md Decision 1
- [X] T004 Refactor `GoogleMapsGisLayerHandle.setMapTypeId` in
      `viewer/layers/gis/GoogleMapsGisLayer.ts` to call
      `map.setOptions({ mapTypeId: MAP_STYLE_TO_GOOGLE_TYPE_ID[mapStyle], styles: mapStyle === 'buildings-only' ? BUILDINGS_ONLY_STYLE : [] })`
      uniformly for all four `MapStyleId` values (depends on T002, T003) — this is
      what makes leaving `'buildings-only'` for any other style clear the hidden
      categories (contracts/viewer-engine-map-style.md)
- [X] T005 [P] Add `'buildings-only'` to `MAP_STYLE_TO_GOOGLE_TYPE_ID` in
      `viewer/layers/gis/GoogleMapsGisLayer.ts`, mapped to
      `google.maps.MapTypeId.ROADMAP` (depends on T002)

**Checkpoint**: `MapStyleId` and the live-map application logic support the new value;
user story work can begin.

---

## Phase 3: User Story 1 - Isolate building footprints from map clutter (Priority: P1) 🎯 MVP

**Goal**: A "Buildings only" option in the map style menu that hides roads, POI,
transit, administrative labels/borders, and natural landscape when selected, and fully
reverts when another style is chosen.

**Independent Test**: Open the map style menu, select "Buildings only", confirm the
named categories disappear and buildings remain visible; select "Road map" and confirm
full reversion.

### Tests for User Story 1

- [X] T006 [P] [US1] Unit test in
      `viewer/layers/gis/GoogleMapsGisLayer.test.ts`: `setMapTypeId('buildings-only')`
      calls `map.setOptions` with `mapTypeId: ROADMAP` and the buildings-only styles
      array
- [X] T007 [P] [US1] Unit test in
      `viewer/layers/gis/GoogleMapsGisLayer.test.ts`: calling `setMapTypeId('roadmap')`
      (or `'satellite'`/`'hybrid'`) after `'buildings-only'` clears `styles` back to `[]`
- [X] T008 [P] [US1] Unit test in `viewer/engine/ViewerEngine.test.ts`:
      `setMapStyle('buildings-only')` updates `viewerEngineStore.mapStyle`, calls
      `activeTarget.applyMapStyle('buildings-only')`, and emits
      `{ type: 'mapStyleChanged', mapStyle: 'buildings-only' }`
- [X] T009 [P] [US1] Extend `viewer/engine/ViewerEngine.contract.test.ts` so the
      existing map-style contract assertions also cover
      `engine.setMapStyle('buildings-only')` returning `{ ok: true, data: undefined }`

### Implementation for User Story 1

- [X] T010 [US1] Add a `'buildings-only'` action (id `'buildings-only'`, label
      "Buildings only", an appropriate `@remixicon/react` icon such as
      `RiBuilding2Line`, `onSelect: () => selectStyle('buildings-only')`,
      `highlighted: mapStyle === 'buildings-only'`) to the `actions` array in
      `useMapStyleControl` in `features/chat/workspaceControls.tsx` (depends on T002)
- [X] T011 [US1] Add/extend a test in `features/chat/workspaceControls.test.tsx`
      asserting the map style menu renders a "Buildings only" action, that selecting it
      calls `viewerEngine.setMapStyle('buildings-only')`, and that it is highlighted
      when `viewerEngineStore.mapStyle === 'buildings-only'`

**Checkpoint**: User Story 1 is fully functional and independently testable — this is
the MVP.

---

## Phase 4: User Story 2 - Style choice persists across a session's map interactions (Priority: P2)

**Goal**: "Buildings only" survives location changes and light/dark theme toggles, the
same way the existing three styles already do.

**Independent Test**: With "Buildings only" active, search a new location and toggle
the theme; confirm the map remains in the buildings-only style both times.

### Tests for User Story 2

- [X] T012 [P] [US2] Unit test in `viewer/engine/MapRenderTarget.test.tsx`: after
      `viewerEngineStore.mapStyle` is `'buildings-only'`, an unrelated store update
      (e.g. a camera or selection change) still results in `handle.setMapTypeId`
      having been called with `'buildings-only'` (covers `applyStoreState`'s
      subscription re-applying the current style on every store change)
- [X] T013 [P] [US2] Unit test in `viewer/engine/MapRenderTarget.test.tsx`: when the
      layer is recreated (simulating the existing theme-toggle-triggered recreation),
      the newly created handle's `setMapTypeId` is called with `'buildings-only'`
      during the mount's initial `applyStoreState()` call

### Implementation for User Story 2

- [X] T014 [US2] Run T012/T013; if either fails, fix `applyStoreState` /
      `registerRenderTarget` wiring in `viewer/engine/MapRenderTarget.tsx` so the
      current `mapStyle` (including `'buildings-only'`) is always re-applied on mount
      and on every store update — expected to already pass unmodified per
      research.md/data-model.md (existing `applyStoreState()` reads `mapStyle` from the
      store on every call), in which case this task is verification-only

**Checkpoint**: User Stories 1 AND 2 both work independently; no regressions to the
existing three styles' persistence behavior.

---

## Phase 5: User Story 3 - Clear behavior when buildings-only styling cannot be applied (Priority: P3)

**Goal**: "Buildings only" is not offered at all in a deployment using vector base-map
rendering (a Map ID is configured), so a user is never left thinking it applied when it
silently did not (FR-006).

**Independent Test**: With `VITE_GOOGLE_MAPS_MAP_ID` set, open the map style menu and
confirm only the original three options are present.

### Tests for User Story 3

- [X] T015 [P] [US3] Unit test in `viewer/api/commands.test.ts` (new file):
      `isBuildingsOnlyStyleSupported()` returns `true` when
      `import.meta.env.VITE_GOOGLE_MAPS_MAP_ID` is unset/empty and `false` when it is
      set (use `vi.stubEnv`)
- [X] T016 [P] [US3] Unit test in `features/chat/workspaceControls.test.tsx`: when
      `isBuildingsOnlyStyleSupported()` is stubbed to return `false`, the map style
      menu's action list contains exactly the original three actions (no
      `'buildings-only'` entry)

### Implementation for User Story 3

- [X] T017 [US3] Add `export function isBuildingsOnlyStyleSupported(): boolean` to
      `viewer/api/commands.ts`, returning `!import.meta.env.VITE_GOOGLE_MAPS_MAP_ID`,
      per research.md Decision 2
- [X] T018 [US3] In `useMapStyleControl` (`features/chat/workspaceControls.tsx`),
      only push the `'buildings-only'` action into `actions` when
      `isBuildingsOnlyStyleSupported()` is `true` (depends on T010, T017)

**Checkpoint**: All three user stories are independently functional; FR-006/SC-003 are
satisfied.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T019 [P] Update the map-style section of `viewer/README.md` to document the
      fourth `'buildings-only'` value and the raster-only gating via
      `isBuildingsOnlyStyleSupported()` — **skipped**: `viewer/README.md` has no
      existing map-style section (its Commands table doesn't even list `setMapStyle`
      for the pre-existing three values), so adding one now would be undocumented scope
      creep unrelated to this feature rather than "updating" something that exists
- [X] T020 Run the full ClientApp test suite (`npm test` from
      `src/AskLucy.Web/ClientApp`) and walk through quickstart.md's manual validation
      steps in both a raster (`VITE_GOOGLE_MAPS_MAP_ID` unset) and vector
      (`VITE_GOOGLE_MAPS_MAP_ID` set) local run

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational. No dependency on US2/US3.
- **User Story 2 (Phase 4)**: Depends on Foundational + US1 (needs the
  `'buildings-only'` value to exist and be selectable to test its persistence).
- **User Story 3 (Phase 5)**: Depends on Foundational + US1 (T018 gates the action
  T010 added).
- **Polish (Phase 6)**: Depends on US1–US3 being complete.

### Within Each User Story

- Tests before implementation (T006–T009 before T010; T012–T013 before T014;
  T015–T016 before T017–T018).
- US2 and US3 both build on US1's menu action (T010) but touch disjoint files
  (`MapRenderTarget.tsx`/test vs. `commands.ts` + `workspaceControls.tsx` gating), so
  they can proceed in parallel once US1 is done.

### Parallel Opportunities

- T003 and T005 (both in `GoogleMapsGisLayer.ts` but non-overlapping additions) can be
  drafted in parallel, though T004 depends on both landing first.
- All test tasks marked [P] within a phase can run in parallel (different files, or
  additive, non-conflicting edits to the same test file only where noted).
- Once Phase 3 (US1) is done, Phase 4 (US2) and Phase 5 (US3) can proceed in parallel.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational).
2. Complete Phase 3 (US1) — the "Buildings only" toggle itself.
3. **STOP and VALIDATE**: run quickstart.md's raster-deployment manual steps.
4. This alone is a demonstrable, shippable increment.

### Incremental Delivery

1. Setup + Foundational → Foundation ready.
2. US1 → validate → MVP.
3. US2 → validate persistence → no regressions to US1.
4. US3 → validate gating in a vector-configured environment → FR-006 satisfied.
5. Polish.
