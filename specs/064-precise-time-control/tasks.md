# Tasks: Precise Time-of-Day Control

**Input**: Design documents from `/specs/064-precise-time-control/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/time-entry.md](./contracts/time-entry.md)

**Tests**: Included and required. The decision logic is deliberately extracted into pure functions
precisely so it can be tested (research D6), and the constitution makes unit-testability
non-negotiable (§2 V). Four of this feature's requirements are refusals — behaviour that only a
test will notice if it regresses to silence.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel — different files, no dependency on an incomplete task
- **[Story]**: US1–US3 from spec.md
- All paths relative to the repository root

## Path Conventions

Frontend-only. Everything is under `src/AskLucy.Web/ClientApp/src/features/solar/`.

---

## Phase 1: Setup

**Purpose**: None required. No new dependency, no new folder, no configuration change.
Intentionally empty; proceeding to Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create the pure module every story adds to, and move the formatting that already exists
into it. All three stories modify `timeEntry.ts` and `SolarTimeControlPanel.tsx`, so this phase
establishes both before any story touches them.

**⚠️ CRITICAL**: No user story phase may begin until T004 passes.

- [X] T001 Create `panels/timeEntry.ts` exporting the named constants from [data-model.md](./data-model.md) — `MINUTES_PER_DAY`, `SNAP_MINUTES`, `MIN_TICK_SPACING_PX`, `MIN_LABEL_SPACING_PX` — each with a TSDoc line recording its origin (§4 magic values)
- [X] T002 Move `formatLocalTime` from `panels/SolarTimeControlPanel.tsx` into `panels/timeEntry.ts` with behaviour unchanged, and import it back into the component
- [X] T003 [P] Create `panels/timeEntry.test.ts` asserting `formatLocalTime` zero-pads and covers both ends of the day
- [X] T004 Run `npx vitest run src/features/solar/panels` and confirm the existing suite still passes after the move — the `aria-valuetext` test pinning `'14:00'` over `'840'` is the one that matters

**Checkpoint**: The module exists, the component imports from it, nothing has changed behaviourally.

---

## Phase 3: User Story 1 — Setting an Exact Minute (Priority: P1) 🎯 MVP

**Goal**: A user can type `06:41` and land on it. The capability that does not currently exist in
any form, and the one specs/063's verification depends on.

**Independent Test**: Type a time, confirm it, check the scene and figures correspond to that exact
minute.

### Tests for User Story 1

- [X] T005 [P] [US1] In `panels/timeEntry.test.ts`, assert `parseLocalTimeEntry` accepts `6:41`, `06:41` and `" 06:41 "`, and that it round-trips against `formatLocalTime` for every valid minute
- [X] T006 [P] [US1] In `panels/timeEntry.test.ts`, assert `abc`, `""` and `6:` return `malformed`, and that `25:00` and `06:75` return `outOfRange` **without clamping** — `25:00` must not become `23:59` (FR-004)
- [X] T007 [P] [US1] In `panels/SolarTimeControlPanel.test.tsx`, assert a committed entry writes through `setLocalMinuteOfDay` and that typing alone does not move the view (FR-002, FR-020)
- [X] T008 [P] [US1] In the same file, assert blur commits rather than discards (US1 scenario 2), and that committing the time already set causes no flicker or reset
- [X] T009 [P] [US1] In the same file, assert a rejected entry retains the typed text, retains the previous time, and renders a visible reason (FR-003, FR-005, FR-024)
- [X] T010 [P] [US1] In the same file, assert committing stops playback (FR-009), and that changing the date clears an uncommitted draft (FR-010)
- [X] T011 [P] [US1] In the same file, assert a spring-forward gap time on an Africa/Cairo date is stated rather than silently resolved to the neighbouring instant, and that a fold time resolves **consistently** across repeated commits without asserting which of the two instants is chosen (FR-007, FR-008)
- [X] T012 [P] [US1] In the same file, assert the field **follows the store**: moving the slider while the field is idle updates the displayed text, and after a typed commit a subsequent slider drag takes over with no reversion to the typed value (US1 scenario 6) — the assertion that stops the draft becoming a stale second reading of the time
- [X] T013 [P] [US1] In the same file, assert an entered time is interpreted in the **site's** timezone, not the browser's: committing `09:00` against the Asia/Dubai fixture must produce an `instantUtc` of 05:00Z (FR-006)

### Implementation for User Story 1

- [X] T014 [US1] Implement `parseLocalTimeEntry` in `panels/timeEntry.ts` returning the discriminated union from [contracts/time-entry.md](./contracts/time-entry.md), never throwing, never clamping
- [X] T015 [US1] Add the three rejection strings to `copy.ts` — malformed, out of range, nonexistent local time — each following the neighbouring `invalidHeight` / `invalidGroundOffset` pattern of naming the bound and saying the previous value was kept (§7, research D7)
- [X] T016 [US1] In `panels/SolarTimeControlPanel.tsx`, replace the read-only `Typography` readout with an editable field holding draft state, retaining the monospace compact styling (FR-022)
- [X] T017 [US1] Implement commit on Enter and on blur, writing through the existing `setLocalMinuteOfDay` — no new store action, no second time value (FR-002, FR-020, research D1)
- [X] T018 [US1] In `panels/SolarTimeControlPanel.tsx`, display `moment.localMinuteOfDay` formatted whenever the field is **not** being edited, so slider, keyboard and playback changes all reach the field — the Idle state of [data-model.md](./data-model.md) (US1 scenario 6)
- [X] T019 [US1] Detect a nonexistent local time by round-tripping the candidate through the existing `fromLocalParts` and `toLocalParts` in `solar/timeZone.ts` and comparing the returned minute; write **no new timezone logic** (FR-007, research D2)
- [X] T020 [US1] Render the rejection inline beneath the row with the field in an error state, keeping the user's text in place (FR-003, FR-005)
- [X] T021 [US1] Stop playback on commit (FR-009, research D8)
- [X] T022 [US1] Clear the draft when `moment.localDate` changes (FR-010, research D9) — the only path by which a draft could apply a time to a date the user did not choose it for
- [X] T023 [US1] Confirm `aria-valuetext` still carries the local time after a typed commit, not only after slider movement (FR-021)

**Checkpoint**: US1 is independently shippable, and unblocks specs/063's verification.

---

## Phase 4: User Story 2 — Seeing Where the Hours Are (Priority: P2)

**Goal**: Tick marks every 15 minutes with hours emphasised — what was actually asked for.

**Independent Test**: Look at the slider; marks present, hours distinguishable, legible rather than
a grey band, and degrading sensibly as the panel narrows.

### Tests for User Story 2

- [X] T024 [P] [US2] In `panels/timeEntry.test.ts`, assert `buildTimeSliderMarks(null)` returns the **Full** tier — unmeasured is normal, not broken (research D5), and jsdom never measures
- [X] T025 [P] [US2] In the same file, assert tier selection derives from `MIN_TICK_SPACING_PX` and `MIN_LABEL_SPACING_PX`: a width giving ≥ 6 px quarter-hour spacing yields Full, a narrower one yields hour-only, a narrower one still yields none (FR-014)
- [X] T026 [P] [US2] In the same file, assert the Full tier emits marks at **exactly 15-minute intervals** — 96 marks from 0 to 1425 inclusive, matching the exact range `snapToQuarterHour` can produce, since the slider's own `max` is 1439 and FR-018 forbids ever landing on 1440 — every value a multiple of `SNAP_MINUTES` — that hour marks are distinguishable from quarter-hour marks in the returned data, and that labels appear only at the chosen interval (FR-011, FR-012). *(Refined during implementation: the original wording said "97 marks from 0 to 1440 inclusive," which would place a mark at a value dragging can never settle on.)*
- [X] T027 [P] [US2] In `panels/SolarTimeControlPanel.test.tsx`, assert marks render on the slider and do not disturb the existing `aria-valuetext` or the panel's two-row structure (FR-013, FR-022)

### Implementation for User Story 2

- [X] T028 [US2] Implement `buildTimeSliderMarks` in `panels/timeEntry.ts` per the tier table in [data-model.md](./data-model.md), returning MUI's mark shape plus an `isHour` distinction
- [X] T029 [US2] In `panels/SolarTimeControlPanel.tsx`, measure the slider track width with `ResizeObserver`, guarded by `typeof ResizeObserver === 'undefined'` and defaulting to unmeasured, following the precedent in `hooks/useWholeRowScroll.ts:30` (research D5)
- [X] T030 [US2] Pass the marks to the Slider and style hour marks more prominently than quarter-hour marks, deriving colour from the theme rather than fixing it, so both themes stay legible (FR-012, FR-015)
- [X] T031 [US2] Confirm in `panels/SolarTimeControlPanel.tsx` that marks do not obscure the handle or the readout at the panel's normal width (FR-013)

**Checkpoint**: The slider is readable. US1 + US2 together cover everything the user asked for.

---

## Phase 5: User Story 3 — Coarse and Fine Control Together (Priority: P2)

**Goal**: Dragging settles on quarter hours; arrow keys still move one minute.

**Independent Test**: Drag and release — lands on a 15-minute boundary. Focus and press an arrow
once — moves exactly one minute.

### Tests for User Story 3

- [X] T032 [P] [US3] In `panels/timeEntry.test.ts`, assert `snapToQuarterHour` returns the nearest multiple of `SNAP_MINUTES`, that `1433` yields `1425` and never `1440`, and that tie-rounding is consistent (FR-018)
- [X] T033 [P] [US3] In `panels/SolarTimeControlPanel.test.tsx`, assert an arrow-key press moves exactly one minute and is **not** snapped — the regression research D3 predicts if source discrimination is dropped (FR-017, SC-006)
- [X] T034 [P] [US3] In the same file, assert **repeated** arrow-key presses each move exactly one minute with none snapped, starting from a value off a 15-minute boundary — the auto-repeat case, where `onChangeCommitted` fires per key-up and a single-press test would not notice a drift (US3 scenario 3, SC-006)
- [X] T035 [P] [US3] In the same file, assert slider movement stops at both ends of the day rather than wrapping (FR-018)

### Implementation for User Story 3

- [X] T036 [US3] Implement `snapToQuarterHour` in `panels/timeEntry.ts`, clamped within the day
- [X] T037 [US3] In `panels/SolarTimeControlPanel.tsx`, record the interaction source on `pointerdown` / `keydown` in a **ref**, not state — it must not cause a render and is not part of the component's output (research D3)
- [X] T038 [US3] Snap in `onChangeCommitted` **only** when the source was a pointer, keeping `step={1}` so MUI's keyboard handling and `aria-valuetext` are untouched (FR-016, FR-017)
- [X] T039 [US3] Confirm in `panels/SolarTimeControlPanel.tsx` that `onChange` remains unsnapped so a drag does not jump when it begins, only settles when it ends (FR-019)

**Checkpoint**: All three stories complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T040 [P] Update `features/solar/README.md`'s module map with `panels/timeEntry.ts` and a note that the panel's time formatting now lives there
- [X] T041 [P] Add a pointer in `specs/052-solar-analysis/spec.md` recording that this feature supersedes its read-only time readout (documentation is part of implementation)
- [X] T042 Run the full frontend suite — `npx vitest run` from `src/AskLucy.Web/ClientApp` — not just the touched files, because page-level tests carry their own assertions about components they render
- [X] T043 Run `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp`; the bare `tsc --noEmit` is a silent no-op under this project's references
- [X] T044 Verify FR-023 by inspection: no solar calculation, scene object, shadow or figure behaviour was touched by any task above. T042's full run, which includes the specs/052 and specs/063 solar suites, is the executable half of SC-008
- [X] T045 Produce the numbered verification steps from [quickstart.md](./quickstart.md) Part 2 for screenshot-based review, leading with Check 1 — typing the reported sunrise in one attempt

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 2 (Foundational)** — blocks everything. T004 is the gate.
- **Phase 3 (US1)** — depends on Phase 2 only.
- **Phase 4 (US2)** — depends on Phase 2 only.
- **Phase 5 (US3)** — depends on Phase 2 only.
- **Phase 6 (Polish)** — after all stories.

### The file-level coupling

The three stories are independent in *behaviour* but all three edit `panels/timeEntry.ts` and
`panels/SolarTimeControlPanel.tsx`. They are therefore **not** safely parallel with each other at
the file level, even though each is separately testable and separately shippable. Run the story
phases in sequence; parallelise within them.

### One ordering constraint inside US1

T018 (the field follows the store) must land before T022 (clear the draft on date change). Clearing
a draft is only meaningful once there is a defined idle display to fall back to.

### Within each phase

Tests before implementation. Tasks marked [P] touch different files or different regions of a test
file and may be written together.

---

## Parallel Execution Examples

**Phase 3 tests**: T005–T013 are nine assertions across two test files — all [P].

**Phase 4 tests**: T024, T025 and T026 are three assertions in one test file, independent of each
other; T027 is in the component's file.

**Phase 5 tests**: T032–T035 are all [P].

**Across stories**: not recommended — see the file-level coupling above.

---

## Implementation Strategy

### MVP

**Phase 2 + Phase 3 (US1)** — 23 tasks. That alone delivers the missing capability: a user can name
an exact minute. It is what unblocks specs/063's verification, and it is independently shippable.

### Incremental delivery

1. Phase 2 → module in place, nothing changed behaviourally
2. + US1 → **ship-worthy**; typing a time works, and 063 is unblocked
3. + US2 → the tick marks that were actually requested
4. + US3 → dragging settles tidily, keys stay fine-grained
5. Polish

### Verification

Per the standing agreement, verification is screenshot-based: numbered mechanical steps with stated
expected values, and the returned screenshots judged rather than the user asked whether the result
looks correct.
