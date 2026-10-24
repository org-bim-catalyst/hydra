---
description: "Task list for solar twilight times and the year day-length chart"
---

# Tasks: Solar Twilight Times & Year Day-Length Chart

**Input**: Design documents from `/specs/085-solar-twilight-daylength/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: included — the specification and this repo's constitution (§10) both require tests with
the behaviour, and the accuracy claims (SC-001 … SC-004) are only checkable by test.

**Organisation**: by user story. US1 (the twilight figures) is the MVP and is independently
testable; US2 (the chart) depends on the same threshold arithmetic but delivers its own value.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies on unfinished tasks)
- **[Story]**: US1 or US2 for the user-story phases only

## Path conventions

All paths are under `src/AskLucy.Web/ClientApp/src/` except the spec files. The feature is
frontend-only: no backend file, migration or endpoint is touched.

---

## Phase 1: Setup

**Purpose**: confirm the baseline this feature builds on is green, so a later failure is the
feature's and not the tree's.

- [x] T001 Run `npx vitest run src/features/solar` from `src/AskLucy.Web/ClientApp` and record the pass count as the baseline. The suite must be green before anything below starts.

---

## Phase 2: Foundational (blocking)

**Purpose**: generalise the rise/set solver so the twilight thresholds and the year profile reuse
it rather than duplicating it (research D4). Both user stories depend on this.

**⚠️ CRITICAL**: no user-story work begins until this phase is complete.

- [x] T002 Write `src/features/solar/solar/crossingSolver.test.ts` — the solver converges on a known crossing; it returns `null` rather than a seed it did not converge to (the existing `maxIterations: 0` seam); it returns `null` when the slope vanishes (a grazing sun); and the instants it returns match `daySummary`'s current sunrise and sunset exactly, which is what proves the extraction is behaviour-preserving.
- [x] T003 Create `src/features/solar/solar/crossingSolver.ts` — move `solveCrossingMs`, its convergence constant, its iteration cap and its finite-difference step out of `daySummary.ts`, parameterised by (a) the quantity driven to zero and (b) its closed-form seed. Export it.
- [x] T004 Switch `src/features/solar/solar/daySummary.ts` to import the shared solver, deleting its private copy. `daySummary.test.ts` must stay green unchanged.

**Checkpoint**: the solver is shared and `daySummary`'s behaviour is provably unchanged.

---

## Phase 3: User Story 1 — the user knows when it gets dark (P1) 🎯 MVP

**Goal**: the Solar Analysis panel reports civil, nautical and astronomical twilight with their
times and lengths, states where a phase does not occur, and states the threshold definition.

**Independent Test**: open the panel on a known site and date; three twilight phases are reported
with times and lengths; the times agree with a published almanac; the altitude figure at each
threshold reads its stated constant; at a high-latitude site in midsummer the phase that does not
occur is stated in words with no time shown.

### Tests for User Story 1

> Write these first; they must fail before T006.

- [x] T005 [P] [US1] Write `src/features/solar/solar/twilight.test.ts` covering: each threshold's two crossings against published almanac values for at least two sites (one mid-latitude, one high-latitude) and both hemispheres; the phase lengths equal the band widths; a threshold not reached reports `reached: false` with no times; a crossing that does not converge reports `undetermined`; and the altitude at each reported crossing reads −5.43° / −11.43° / −17.43° within tolerance.
- [x] T006 [P] [US1] Extend `src/features/solar/panels/solarFiguresContent.test.ts` for: the three twilight rows in order; the "does not occur" wording naming the threshold; the threshold-definition statement; and that the existing sunrise, sunset, day-length, polar and accuracy rows are byte-for-byte unchanged.

### Implementation for User Story 1

- [x] T007 [US1] Create `src/features/solar/solar/twilight.ts` — `TWILIGHT_THRESHOLDS` (6°, 12°, 18°, on the sun's centre, geometric); the geometric→apparent target conversion using `HORIZON_REFRACTION_DEGREES` (research D2); `twilightCrossings(dateUtc, latitude, longitude)` returning the crossings, the per-phase lengths and the `reached`/`undetermined` flags (research D5, D6); and a memoised per-(site, date) cache so a playback tick does not re-solve the same day.
- [x] T008 [P] [US1] Add the new strings to `src/features/solar/copy.ts` — the phase labels, the "does not reach N° below the horizon" statement, the "could not be determined" statement, the threshold-definition statement, and the three altitude constants as a formatter taking the tolerance constant rather than a literal.
- [x] T009 [US1] Extend `src/features/solar/panels/solarFiguresContent.ts` — add the crossings to `SolarFiguresInput`, emit the three twilight rows after the existing ones, and add the threshold-definition and not-occurs statements to the closing text. Keep every existing row and statement untouched.
- [x] T010 [US1] Pass the day's twilight summary through `src/AskLucy.Web/ClientApp/src/features/solar/components/SolarAnalysisOverlay.tsx` — compute it from the memoised `twilightCrossings` (not a fresh solve per tick) and hand it to `buildSolarFiguresContent` alongside the existing `daySummary`.

**Checkpoint**: US1 is complete and testable on its own; the chart does not exist yet and nothing else changed.

---

## Phase 4: User Story 2 — the user sees the whole year at once (P2)

**Goal**: a live panel beside the figures draws every day of the selected date's year — night, the
three twilight phases and daylight in order — with the selected date marked, the latitude and
hemisphere stated, and the year's longest and shortest day in text.

**Independent Test**: open the chart panel for a site; it covers every day of the year; the
daylight band is widest in the site's summer; the selected date is marked; clicking anywhere
changes nothing; the chart's band for a chosen day matches that day's stated sunrise and sunset.

### Tests for User Story 2

- [x] T011 [P] [US2] Extend `src/features/solar/solar/twilight.test.ts` (or add `yearProfile.test.ts`) for the year profile: one entry per day (365 and 366); contiguous segments covering the local day; a polar-night day as a single night segment and a midnight-sun day as a single day segment; the longest and shortest day computed from the same daylight minutes the chart draws; and that two reads for the same site and year return the same object instance (the cache, research D9).
- [x] T012 [P] [US2] Write `src/features/solar/panels/SolarDayLengthPanel.test.tsx` — renders one row per day, marks the selected date, states the latitude and hemisphere, carries a text description for assistive technology, and changes nothing when clicked (no store write, no time change, no selection change).
- [x] T013 [P] [US2] Write `src/features/solar/panels/SolarDayLengthPanel.a11y.test.tsx` — the panel's automated accessibility check passes in the light and dark themes.

### Implementation for User Story 2

- [x] T014 [US2] Add `yearProfile(latitude, longitude, year, timeZoneId)` to `src/features/solar/solar/twilight.ts` — build one day phase sequence per day from `twilightCrossings`, resolve the longest and shortest day, derive the hemisphere, and memoise by (site, year). Keep the DST case honest: measure segments in local minutes from the local day's start, so a 23- or 25-hour day is contiguous and proportional (research D12, spec FR-017 as amended).
- [x] T015 [P] [US2] Add the chart's strings and phase colours to `src/features/solar/copy.ts` — the panel title, the caption formatter (latitude + hemisphere), the "longest/shortest day" labels, the chart's accessible description, and the five phase names used as band labels.
- [x] T016 [US2] Add the chart panel's type key and its zod data schema to `src/features/solar/panels/panelContracts.ts`, following `SOLAR_TIME_CONTROL_TYPE_KEY`'s shape.
- [x] T017 [US2] Create `src/features/solar/panels/SolarDayLengthPanel.tsx` — a canvas chart, one row per day of the year, each row's phases in order with colours resolved from the MUI theme (research D11), the selected date's row marked, the latitude and hemisphere caption, and a text description for assistive technology. It reads the site and date from `solarAnalysisStore` and writes nothing (FR-015).
- [x] T018 [US2] Register and open the panel in `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/solarAnalysisExtension.tsx` — `registerLivePanelKind` with compact chrome, and an `openPanel` in `activate()` beside the existing two, with a request id constant.

**Checkpoint**: both stories work independently; the chart can be closed without affecting the figures.

---

## Phase 5: Polish & cross-cutting

- [ ] T019 Run the full solar suite (`npx vitest run src/features/solar`) and the full frontend suite (`npx vitest run` from `src/AskLucy.Web/ClientApp`). Known flakes: the ChatPage tests time out only under parallel load — rerun them alone before treating a failure as real.
- [ ] T020 Run `npx tsc -b --noEmit` (never a bare `tsc --noEmit`) and `npx eslint src/features/solar` from `src/AskLucy.Web/ClientApp`; both clean.
- [x] T021 Update the module map in `src/features/solar/README.md` with `crossingSolver.ts`, `twilight.ts` and `SolarDayLengthPanel.tsx`, and record the twilight definition (centre, geometric, 6°/12°/18°) beside the existing tolerance notes.
- [ ] T022 Walk [quickstart.md](quickstart.md) checks 1–10 against a running app, recording the almanac reference values used for check 1 in `twilight.test.ts` so the check is against a published source rather than a recomputed value. Report the screenshots for the panel-content checks.

---

## Dependencies & execution order

### Phase dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: after T001 — blocks both user stories.
- **US1 (Phase 3)**: after T004.
- **US2 (Phase 4)**: after T004. It shares `twilight.ts` with US1, so T014 is written after T007 in practice even though it belongs to a different story; a second person can start on T015/T016 in parallel.
- **Polish (Phase 5)**: after both stories.

### Within each story

- Tests first (T005/T006, T011–T013), and they must fail before the implementation they cover.
- Pure module before the panel that renders it.
- Copy before the component that uses it.
- Registration last, so a broken panel never ships registered.

### Parallel opportunities

- T005 and T006 are independent files.
- T008 can be written while T007 is in progress (different files).
- T011, T012, T013 are three independent test files.
- T015 and T016 are independent of each other and of T014.

---

## Implementation strategy

**MVP first**: Phases 1–3 deliver US1 — the twilight figures — which is a complete, shippable
improvement on its own. Validate it against the quickstart before starting the chart.

**Then US2**: the chart is additive; it reads the same threshold arithmetic and writes nothing, so
it can ship after US1 has been checked in the running app.

**One commit per logical group**: the solver extraction (T002–T004), the twilight figures
(T005–T010), the chart (T011–T018), the polish (T019–T022).

---

## Notes

- `[P]` means a different file and no dependency on unfinished work.
- Nothing in this feature touches the backend, the database, the shared panel content vocabulary,
  its JSON Schema artifact, or the server's model-facing schema (research D7, D8). A diff that
  reaches any of them is a signal the plan was not followed.
- The threshold figures are compared against a **published** almanac, never against a value
  recomputed by the same code under test (quickstart check 1).
