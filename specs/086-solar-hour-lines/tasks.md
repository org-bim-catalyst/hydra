---
description: "Task list for Solar Hour Lines (Analemma Curves)"
---

# Tasks: Solar Hour Lines (Analemma Curves)

**Input**: Design documents from `/specs/086-solar-hour-lines/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/solar-hour-lines.md](contracts/solar-hour-lines.md), [quickstart.md](quickstart.md)

**Tests**: requested by the specification (FR-004, FR-005, FR-009 and SC-006 name the tests), so every user story includes test tasks.

**Organization**: tasks are grouped by user story. The three stories share `sunPathCurve.ts`, `SolarScene.ts` and the one test file, so the stories are **sequential** (P1 → P2 → P3); the [P] marker is used only where tasks touch different files.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different file, no dependency on an incomplete task)
- **[Story]**: US1, US2 or US3 — omitted in Setup, Foundational and Polish
- Every task names its exact file path

## Path Conventions

All paths are under `src/AskLucy.Web/ClientApp/src/features/solar/` unless stated otherwise.

---

## Phase 1: Setup

**Purpose**: establish a green baseline before touching shared solar code

- [X] T001 Run `npx vitest run src/features/solar` from `src/AskLucy.Web/ClientApp` and confirm the existing suite passes; record any pre-existing failure so it is not mistaken for a regression later

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the one change every story's build path depends on — the zone formatter memo

**⚠️ CRITICAL**: the fixed-furniture build must not stall the main thread; this lands first so every later task is measured against the real cost

- [X] T002 In `solar/timeZone.ts`, add a module-scoped `Map<string, Intl.DateTimeFormat>` and build the formatter once per zone id inside `formatParts`; keep the options, the fixed-point loop and every public result unchanged (research D6, FR-010). Verify `npx vitest run src/features/solar/solar/timeZone.test.ts` is green with no test edits.

**Checkpoint**: the existing solar suite is still green and the build path costs ~35 ms of formatting instead of ~476 ms.

---

## Phase 3: User Story 1 — Read the Sun's Path for Any Hour of the Year (Priority: P1) 🎯 MVP

**Goal**: one thin, dim, theme-coloured curve per local clock hour on the dome, with the existing hour marks sitting on their curves.

**Independent Test**: draw the dome at Dubai and find the "12" mark lying on the 12:00 curve; confirm curves exist for daytime hours and are visibly thinner and dimmer than the monthly lattice, the chosen day and the two extremes.

### Tests for User Story 1 ⚠️

> Write these first; they fail until T006–T008 land.

- [X] T003 [US1] In `scene/sunPathCurve.test.ts`, add a test that, for Dubai (25.2, 55.3, `Asia/Dubai`), London (51.5, −0.1, `Europe/London`), Sydney (−33.87, 151.21, `Australia/Sydney`), Singapore (1.35, 103.82, `Asia/Singapore`) and 78°N (78, 15.6, `Arctic/Longyearbyen`) over 2026, every point of every returned run has `z >= -1e-6` (FR-005, SC-003)
- [X] T004 [US1] In `scene/sunPathCurve.test.ts`, add a test that at Dubai, London and Sydney the position of the 12:00 sprite from `buildDatedPath` equals one sample of the 12:00 curve from `sampleHourLineRuns` to within `1e-6` (FR-009, SC-006)
- [X] T005 [US1] In `scene/sunPathCurve.test.ts`, add a test that `buildHourLines` at Dubai returns no mesh with `userData.localHour === 0` and at least one with `userData.localHour === 12`, that no hour never-up at midnight produces a mesh, and that every returned mesh is a `THREE.Mesh` with `TubeGeometry` and not a `THREE.Line` (FR-001, FR-006)

### Implementation for User Story 1

- [X] T006 [US1] In `scene/sunPathCurve.ts`, add `HOUR_LINE_TUBE_RADIUS = 0.15` and `HOUR_LINE_OPACITY = 0.5` (research D8); implement `sampleHourLineRuns(hour, latitude, longitude, year, timeZoneId, radiusMetres): THREE.Vector3[][]` sampling every calendar day of `year` (365 or 366) with `solarPosition(fromLocalParts(localDate, hour * 60, timeZoneId), …)` and keeping only `altitudeDegrees >= 0` (FR-001, FR-002, FR-005, FR-009, FR-010, FR-014); implement `buildHourLines(latitude, longitude, year, timeZoneId, radiusMetres, color): THREE.Mesh[]` building one tube per run through `buildTubeFromPoints`, setting `userData.localHour`, and drawing nothing for an hour with no above-horizon samples (FR-003, FR-006, FR-009). Until US3 lands, return the above-horizon samples as a single run.
- [X] T007 [US1] In `scene/sunPathCurve.ts`, add `hourLines: THREE.Mesh[]` to `FixedFurnitureObjects` and extend `buildFixedFurniture` to `(latitude, longitude, year, timeZoneId, hourLineColor, radiusMetres?)`, building and returning the hour lines (contracts/solar-hour-lines.md)
- [X] T008 [US1] In `scene/SolarScene.ts`, extend `updateSunPath` with `timeZoneId` and `hourLineColorCss`; convert the CSS colour once with `new THREE.Color(css).getHex()`; pass both to `buildFixedFurniture`; add `...furniture.hourLines` to `sunPathFixedGroup` between the monthly arcs and the shell; update `scene/sunPathCurve.test.ts` and any other direct call sites for the new signatures
- [X] T009 [US1] In `components/SolarAnalysisOverlay.tsx`, read `useTheme()` from `@mui/material` and pass `theme.palette.text.secondary` as the new `updateSunPath` argument; add `theme.palette.text.secondary` to the dome effect's dependency list (FR-003, research D7)

**Checkpoint**: at Dubai the dome shows the hour curves, the "12" mark sits on its curve, and no curve is a hardcoded colour. This is the MVP.

---

## Phase 4: User Story 2 — Nothing Rebuilds While Scrubbing or Playing (Priority: P2)

**Goal**: the curves are fixed furniture whose identity survives every date and time-of-day change, and whose clock corrects itself once when the site's time zone resolves.

**Independent Test**: capture the fixed group's children, scrub a year of dates and a full day of times, and confirm the children are the same objects; then change only the time zone and confirm one rebuild.

### Tests for User Story 2 ⚠️

- [X] T010 [US2] In `scene/sunPathCurve.test.ts`, add a test that the fixed group contains at least one mesh carrying `userData.localHour` after a draw, and that its child identities are unchanged across a year of date changes and a day of time-of-day changes (FR-007, SC-002)
- [X] T011 [US2] In `scene/sunPathCurve.test.ts`, add a test that calling `updateSunPath` with the same site and year but a different `timeZoneId` returns `true` and replaces the fixed group's children exactly once (FR-008)
- [X] T012 [US2] In `scene/sunPathCurve.test.ts`, add a test that a colour-only change (same site, year and zone, different `hourLineColorCss`) returns `true`, keeps the same hour-line mesh objects, and changes their material colour (FR-003)

### Implementation for User Story 2

- [X] T013 [US2] In `scene/SolarScene.ts`, add `timeZoneId` to the fixed rebuild key: `` `${site}|${year}|${radius}|${timeZoneId}` `` (FR-008)
- [X] T014 [US2] In `scene/SolarScene.ts`, store the built hour-line meshes and their colour hex; when the fixed key is unchanged but the colour differs, re-tint the existing meshes' `MeshBasicMaterial` colours in place, update the stored hex and return `true` so the caller invalidates — never rebuild the geometry (FR-003, research D7)

**Checkpoint**: scrubbing and playback rebuild nothing; a time zone resolving after the first draw corrects the curves once; a theme toggle re-tints without a rebuild.

---

## Phase 5: User Story 3 — Correct at the Horizon and at the Poles (Priority: P3)

**Goal**: above-horizon stretches are drawn as separate runs, and hours that never see the sun draw nothing.

**Independent Test**: Sydney, 2026, local 06:00 produces three separate curves; the pure splitter returns two runs for `[up, up, down, up]`; no run contains a below-horizon sample.

### Tests for User Story 3 ⚠️

- [X] T015 [US3] In `scene/sunPathCurve.test.ts`, add a pure test for `splitAboveHorizonRuns`: `[a, b, null, c]` → `[[a, b], [c]]`, `[]` → `[]`, `[null, null]` → `[]`, and a run never spans a `null` (FR-004)
- [X] T016 [US3] In `scene/sunPathCurve.test.ts`, add a real-data test that at Sydney over 2026, `sampleHourLineRuns` returns exactly three runs for hour 6 and for hour 7, and two runs for hour 6 at London (FR-004, SC-003, research D11)

### Implementation for User Story 3

- [X] T017 [US3] In `scene/sunPathCurve.ts`, add and export `splitAboveHorizonRuns<T>(samples: ReadonlyArray<T | null>): T[][]` (a `null` closes the current run, never joins across it); make `sampleHourLineRuns` push `null` for below-horizon days and split through it; `buildHourLines` draws nothing for a run shorter than two points (FR-004, FR-006, contracts/solar-hour-lines.md)

**Checkpoint**: all three stories work; no tube crosses a horizon gap, and hours that never see the sun draw nothing.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T018 [P] Add the amendment pointer to `specs/063-solar-accuracy-performance/contracts/solar-scene.md`: the fixed-furniture row gains the hour lines, and the fixed rebuild condition gains the time zone
- [X] T019 [P] Update the module map in `src/AskLucy.Web/ClientApp/src/features/solar/README.md` for `sunPathCurve.ts` (hour lines) and `timeZone.ts` (formatter memo)
- [X] T020 Run the full validation: `npx vitest run src/features/solar`, `npx tsc -b --noEmit`, `npm run lint` from `src/AskLucy.Web/ClientApp`; confirm `git diff --stat` shows no backend file and no file under `features/solar/solar/` other than `timeZone.ts` (FR-013)
- [X] T021 Walk [quickstart.md](quickstart.md) checks 1–8 in the running app and record the result — **2026-10-24**: check 1 confirmed on the running dome (the hour curves appear and read as background furniture). Checks 2, 3 and 6 are asserted by `sunPathCurve.test.ts` (mark-on-curve at three sites, fixed-group identity across dates and times, colour re-tint without a rebuild) rather than eyeballed; checks 4, 5, 7 and 8 are covered by the tests and the recorded measurements in research D6/D11.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup; blocks every story (the build-cost memo)
- **User Story 1 (Phase 3)**: depends on Foundational
- **User Story 2 (Phase 4)**: depends on US1 (the curves must exist before their lifetime can be asserted)
- **User Story 3 (Phase 5)**: depends on US1 (the sampler exists); can be done before or after US2, but both touch `sunPathCurve.ts` / `SolarScene.ts` so run them sequentially
- **Polish (Phase 6)**: depends on all three stories

### Within Each User Story

- Tests first, and they must fail before the implementation task lands
- Builder before scene wiring before the overlay
- One file at a time: `sunPathCurve.ts` → `SolarScene.ts` → `SolarAnalysisOverlay.tsx`

### Parallel Opportunities

This feature is deliberately small: three source files and one test file. Genuine parallelism is limited to the two documentation tasks.

```bash
# Polish, different files:
Task: "Add the amendment pointer to specs/063-solar-accuracy-performance/contracts/solar-scene.md"
Task: "Update the module map in features/solar/README.md"
```

### Story order

```
T001 → T002 → T003..T009 (US1) → T010..T014 (US2) → T015..T017 (US3) → T018..T021
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. T001 baseline, T002 formatter memo
2. T003–T009: the curves exist, are theme-coloured, and the 12:00 mark sits on the 12:00 curve
3. **STOP and validate**: open the dome at Dubai and confirm check 1 and check 2 of quickstart.md
4. Deploy/demo if ready — US1 alone is a visible, correct-by-the-spec increment (gaps may still be joined until US3)

### Incremental Delivery

1. US1 → the curves appear (MVP)
2. US2 → they stay put while scrubbing, and correct themselves once when the zone resolves
3. US3 → they stop at the horizon and absent hours draw nothing
4. Polish → docs, full suite, quickstart walk-through

### Notes

- The last task of each story is a checkpoint: stop and validate before the next.
- `fromLocalParts` is the shared conversion; never re-implement it for the curves.
- The colour is never defaulted in the scene — a missing argument must fail at compile time, not fall back to a literal.
