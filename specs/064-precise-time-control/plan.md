# Implementation Plan: Precise Time-of-Day Control

**Branch**: `064-precise-time-control` | **Date**: 2026-09-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/064-precise-time-control/spec.md`

## Summary

Three additions to one existing panel: a typed time entry, 15-minute tick marks on the slider, and
drag-snapping paired with one-minute keyboard stepping. One component changes, one pure module is
added, and a handful of strings join `copy.ts`.

The research phase found two of the four hard parts already solved in the repository. The store's
`setLocalMinuteOfDay` already recomputes `instantUtc` and writes one moment, so the single-source
rule of specs/052 costs nothing to preserve (D1). `timeZone.ts`'s `fromLocalParts` already resolves
daylight-saving transitions by fixed-point convergence, and a round trip through it detects the
spring-forward gap without a line of new timezone logic (D2).

The two that are genuinely awkward are MUI's Slider — whose single `step` property cannot express
"snap on drag, one minute on arrow keys", resolved by discriminating the interaction source (D3) —
and tick legibility on a user-resizable panel, resolved by deriving the tier from measured width
against named minimum-spacing constants rather than from guessed breakpoints (D4).

This feature is sequenced **before** the implementation of specs/063, because 063's verification step
is "set the time to the sunrise the panel reports" and the current control cannot reliably do that.

## Technical Context

**Language/Version**: TypeScript 5.x (`strict`), React 19

**Primary Dependencies**: MUI (existing), Zustand (existing). **No new package.**

**Storage**: N/A — no persisted state, no API, no backend.

**Testing**: Vitest + Testing Library. The decision logic is extracted into pure functions so it is
unit-testable without simulating pointer drags in jsdom (D6), following the existing test file's
practice of driving the slider with `fireEvent.change`.

**Target Platform**: Browser; the solar Time of Day panel inside the viewer panel framework.

**Project Type**: Frontend-only change to a single existing feature-domain folder.

**Performance Goals**: None beyond not regressing. The Full tier adds 96 mark elements to one
slider; this is a static list rebuilt only on resize.

**Constraints**: specs/052 FR-022's single-source rule holds (FR-020). The `aria-valuetext` local
time announcement is preserved on every route (FR-021). The compact two-row layout stands (FR-022).
No solar calculation, scene, shadow or figure behaviour changes (FR-023).

**Scale/Scope**: 2 existing files modified, 1 added; 1 existing test file extended, 1 added.

## Constitution Check

*GATE: passed before Phase 0, re-checked after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | N/A — entirely within `ClientApp/src/features/solar/panels`, crossing no layer boundary. No backend code. |
| **II. SOLID** | Improved. `SolarTimeControlPanel.tsx` currently owns its own time formatting; D6 moves formatting, parsing, snapping and tier selection into `timeEntry.ts`, leaving the component with wiring only (SRP). |
| **III. Simplicity / YAGNI** | The 15-minute interval is fixed, not configurable — no evidence a second interval is wanted (spec Assumptions). Range selection is explicitly out of scope (D10). No abstraction introduced for a hypothetical second entry format. |
| **IV. Composition** | Preserved — pure functions and one component, no hierarchy. |
| **V. Dependency Inversion & Testability** | The reason for D6. Every decision is a pure function of its arguments, testable with no DOM and no pointer simulation. |
| **VI. Separation of Concerns** | Preserved. The panel continues to write through the store's setter; no time arithmetic moves into the component. |
| **VII. Convention Over Configuration** | Follows the panel's existing compact styling vocabulary and the existing `copy.ts` string convention. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | The feature's main new failure surface. FR-003, FR-004, FR-007 and FR-024 require every rejected or impossible entry to be stated. D7 adopts the wording pattern `BuildingCorrectionsPanel` already uses for exactly this — name the bound, say the previous value was kept. |
| **§4 TypeScript** | `strict` already on; no `any`. The parse result is a discriminated union so an unhandled refusal is a compile error, not a runtime surprise. |
| **§4 Magic values** | `MIN_TICK_SPACING_PX`, `MIN_LABEL_SPACING_PX`, `SNAP_MINUTES` and `MINUTES_PER_DAY` are named exported constants. |
| **§7 User-facing strings** | Every new string lands in `copy.ts`, none inline. |
| **§5 Database / §6 API** | N/A — neither is touched. |

**Gate result: PASS.** No violations to justify; the Complexity Tracking table is omitted.

## Project Structure

### Documentation (this feature)

```text
specs/064-precise-time-control/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — D1..D10
├── data-model.md        # Phase 1 — the draft entry and its states
├── quickstart.md        # Phase 1 — validation, manual half screenshot-ready
├── checklists/
│   └── requirements.md  # Spec quality checklist (passing)
├── contracts/
│   └── time-entry.md    # The pure module's contract
└── tasks.md             # Phase 2 — NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/solar/
├── panels/
│   ├── timeEntry.ts                    ADDED — parse, format, snap, tick tiers (D6)
│   ├── timeEntry.test.ts               ADDED — the pure-function suite
│   ├── SolarTimeControlPanel.tsx       MODIFIED — editable readout, ticks, snap-vs-keys wiring
│   └── SolarTimeControlPanel.test.tsx  MODIFIED — entry, rejection, DST gap, stale draft
└── copy.ts                             MODIFIED — rejection wording (D7), DST-gap wording (FR-007)
```

**Structure Decision**: Frontend-only, inside the existing `features/solar/panels` folder. No new
folder, no new dependency, no change to the store, the scene, or the solar modules.

## Phase 0 — Research

Complete. See [research.md](./research.md) for D1–D10. No `NEEDS CLARIFICATION` markers entered
planning; the specification's three open questions were resolved during drafting and recorded in its
Clarifications section.

## Phase 1 — Design & Contracts

Complete. [data-model.md](./data-model.md) defines the draft entry, its states and the rule that
keeps it from becoming a second source of truth; [contracts/time-entry.md](./contracts/time-entry.md)
defines the pure module's four functions; [quickstart.md](./quickstart.md) is the validation guide,
written for screenshot-based verification against stated expected values.

### Post-design Constitution re-check

**PASS.** The design adds one pure module and modifies one component. It introduces no dependency,
no layer, no state, and no abstraction without a present need. The one item needing attention —
§2 VIII, which this feature exercises more than most, since a time entry can fail in four distinct
ways — is carried as explicit contract requirements rather than left to implementation discretion.

## Risks

| Risk | Mitigation |
|---|---|
| `onChangeCommitted` source discrimination proves unreliable across input devices | The fallback is benign: a missed discrimination snaps a keyboard step to 15 minutes, which is visible and testable rather than silent. Tests assert arrow-key movement is exactly one minute (SC-006), so a regression fails the suite rather than reaching the user. |
| 96 mark elements degrade slider interaction | Marks are static and rebuilt only on resize, not per frame. If measurement shows otherwise, the Reduced tier already exists as the documented fallback and can be made the default without new design. |
| The DST-gap round trip has an off-by-one at the fold | D2's round trip proves *existence*, not which of two instants was chosen. The fold is explicitly delegated to `fromLocalParts`'s existing deterministic convergence (FR-008), and a test pins that it resolves consistently rather than asserting which of the two it picks. |
| A stale draft applies a time to the wrong date | D9 clears the draft on date change. Called out as its own decision because it is the easiest requirement here to omit and the only one that produces a wrong answer rather than a refusal. |
| Ticks illegible in dark theme | FR-015 and a quickstart step; mark colour derives from the theme like the rest of `compactStyles`, rather than being fixed as the amber accent is. |
