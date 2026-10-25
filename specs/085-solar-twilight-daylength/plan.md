# Implementation Plan: Solar Twilight Times & Year Day-Length Chart

**Branch**: `085-solar-twilight-daylength` | **Date**: 2026-10-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/085-solar-twilight-daylength/spec.md`

## Summary

Two additions to the solar analysis panel, both built on the solar module's existing position
mathematics rather than on any new astronomy:

1. **Twilight figures.** The Solar Analysis panel gains the three standard twilight thresholds —
   the sun's centre at 6°, 12° and 18° below the horizon — as reported times, as the length of
   each phase, and as a plain statement where a phase does not occur on the selected date.
2. **A year-long day-length chart.** A panel beside the figures draws every day of the year at
   the selected site, with night, the three twilight phases and daylight in order, and the
   selected date marked.

The thresholds are solved by the same Newton solver the rise/set crossing already uses,
generalised to take a target altitude; the year profile is computed once per site and year and
cached, so neither costs anything while the user scrubs or plays the day.

## Technical Context

**Language/Version**: TypeScript 5.x (React 19, Vite) on .NET 10 / C# 14 backend
**Primary Dependencies**: none added — the solar module's own ported NOAA algorithm, the MUI
theme, and the platform's own panel framework
**Storage**: none — computed in the browser, session-scoped, never persisted
**Testing**: vitest (frontend), xUnit (backend, unchanged by this feature)
**Target Platform**: Web (desktop and mobile breakpoints)
**Project Type**: web-service + single-page frontend; this feature is **frontend-only**
**Performance Goals**: the figures panel's per-tick rebuild must not get measurably slower
(SC-009); a year profile must be available within one second of the site being established
(SC-008)
**Constraints**: no new runtime dependency; the panel's compact layout and accessibility
commitments (specs/052 FR-032) are unchanged; the shared content vocabulary is **not** extended
(research D7, D14)
**Scale/Scope**: one feature panel and one figures panel; ~5 new/changed frontend files, 0
backend files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicable | Assessment |
|---|---|---|
| §2.I Clean Architecture | No | Frontend-only. The solar module is self-contained under `features/solar/` and depends on the viewer's panel framework, never the reverse. No new cross-layer edge is introduced. |
| §2.III Simplicity / YAGNI | Yes | No new dependency, no new shared abstraction, no server round trip, no persistence. The crossing solver is generalised once rather than duplicated for thresholds (research D4); the year profile is computed once and cached (D9). |
| §2.VI Separation of Concerns | Yes | The threshold arithmetic lives in `features/solar/solar/`; the panel files carry only presentation and the copy strings live in `copy.ts` as the constitution's §7 i18n rule requires. |
| §2.VII Convention over Configuration | Yes | The new panel is a **live panel** registered through the extension's existing `registerLivePanelKind` path, exactly like the Time of Day and Building Corrections panels (specs/052 research D12). The shared content vocabulary, its version and the server's model-facing schema are left alone (research D7, D14). |
| §2.VIII No Silent Failures | Yes | A threshold that is not reached, and a crossing that does not converge, are both stated in the panel in words — never omitted, never shown as an approximation (spec FR-006, FR-008). |
| §3 Architecture Rules | No | Frontend-only; the backend is untouched, so the layer rules and the Application/Infrastructure boundary are not engaged. |
| §7 UI Principles | Yes | The chart is composed from the MUI theme (colours resolved by the composer, research D11) and from the platform's own panel chrome; it is keyboard-inert and carries a text description (spec FR-019); all user-facing copy goes to `copy.ts`. |
| §10 Testing Standards | Yes | The threshold solver, the phase lengths, the polar branches and the year profile are unit-tested with no DOM; the panel's rendering and accessibility are covered by the existing panel test files. Tests ship with the behaviour, in the same change. |
| §13 Documentation | Yes | This plan, the contracts, the quickstart and the spec amendment ship with the code; specs/052's contract gains the new figures. |

**Gate result**: pass. No complexity tracking is required — nothing here needs a justification
beyond the decisions recorded in research.md.

## Project Structure

### Documentation (this feature)

```text
specs/085-solar-twilight-daylength/
├── spec.md              # amended 2026-10-23 (FR-020, FR-017, FR-021) with reasons
├── plan.md              # This file
├── research.md          # Phase 0 output — 15 decisions with alternatives
├── data-model.md        # Phase 1 output — the entities the feature introduces
├── quickstart.md        # Phase 1 output — how to verify it end to end
├── contracts/
│   ├── solar-twilight.md          # the figures and the chart panel, as a contract
│   └── solar-panels-amendment.md  # the amendment to specs/052's panel contract
└── tasks.md             # Phase 2 output (/speckit-tasks) — NOT created here
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/solar/
├── solar/
│   ├── crossingSolver.ts   # NEW — the Newton solver, generalised to a target altitude
│   ├── twilight.ts         # NEW — thresholds, per-day crossings, phase lengths, year profile
│   └── daySummary.ts       # MODIFIED — imports the generalised solver instead of owning one
├── panels/
│   ├── SolarDayLengthPanel.tsx  # NEW — the chart, drawn on a canvas
│   ├── solarFiguresContent.ts   # MODIFIED — the twilight rows
│   └── panelContracts.ts         # MODIFIED — the chart panel's type key and data schema
├── components/
│   └── SolarAnalysisOverlay.tsx  # MODIFIED — passes the day's crossings to the figures
└── copy.ts                # MODIFIED — every new user-facing string

src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/
└── solarAnalysisExtension.tsx     # MODIFIED — registers and opens the chart panel

specs/052-solar-analysis/contracts/
└── solar-panels.md               # MODIFIED — the figures panel gains the twilight rows
```

**Structure Decision**: the feature lives entirely inside the existing `features/solar/` module,
which already owns the solar arithmetic, the panel copy, and the two live panels. The only file
outside it that changes is the extension registration shim — the one place the viewer knows the
solar feature exists, which specs/049 requires to stay thin. No new top-level directory, no new
project, no new dependency.

## The one design decision that changes the specification

The chart was specified as a new kind in the shared panel content vocabulary, presented inside the
Solar Analysis panel. Two findings during planning changed that:

- **The content vocabulary is an interchange format with a server-side contract.** A new kind
  means a version increment, which means the client's envelope, the committed JSON Schema
  artifact and the server's model-facing schema all move together — a change every panel in the
  product depends on, made for a chart only solar uses (research D7, D8).
- **The figures panel's document is rebuilt and re-parsed on every tick of playback.** A year's
  worth of per-day boundaries inside that document would be re-validated and re-rendered every
  frame, directly against spec FR-016 and SC-009 (research D9, D10).

The chart is therefore a **live panel** — the same mechanism the Time of Day and Building
Corrections panels already use, and the same shape the reference tool uses for its own day-length
chart — registered by the solar extension and opened alongside the figures. The shared vocabulary
is not touched at all, which is research D14 and spec FR-021 as amended.

The spec's FR-020, FR-017 and FR-021 are amended in place with the reasons, dated 2026-10-23.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. No violation needed a justification.

## Phase 0 — research

Complete: [research.md](research.md). Fifteen decisions, each with the alternatives considered.
The load-bearing ones for the implementation:

| Decision | Consequence for the code |
|---|---|
| D1 | Thresholds are the sun's **centre** at −6°/−12°/−18°, geometric. |
| D2 | A crossing is solved as the instant the **apparent** centre altitude equals `threshold + HORIZON_REFRACTION_DEGREES`. |
| D3 | The panel states that the thresholds are unrefracted, so the altitude shown beside them reads −5.43°/−11.43°/−17.43° everywhere. |
| D4 | The Newton solver moves to `crossingSolver.ts` and takes a target altitude; `daySummary.ts` imports it. |
| D5 | A phase's length is the band it occupies, measured on the evening side. |
| D6 | Each threshold's own hour-angle bound decides whether it is reached; the polar statement is untouched. |
| D7/D14 | The chart is a **live panel**; the shared content vocabulary is not extended. |
| D9 | The year profile is computed once per (site, year) and cached. |
| D10 | The chart is drawn on a canvas, one row per day. |
| D11 | The chart's payload is generic (rows of segments in named series) and the colours come from the composer. |

## Phase 1 — design

Complete: [data-model.md](data-model.md), [contracts/solar-twilight.md](contracts/solar-twilight.md),
[contracts/solar-panels-amendment.md](contracts/solar-panels-amendment.md),
[quickstart.md](quickstart.md).

### The shape of the work

**Backend**: none. The solar module is client-side by design (specs/052 research D1) and this
feature adds no server state, no endpoint and no model-facing contract change.

**Frontend, in dependency order**:

1. `solar/crossingSolver.ts` — the Newton solver, generalised to a target altitude and a
   geometric seed; `daySummary.ts` switches to it and its own tests stay green.
2. `solar/twilight.ts` — the three thresholds, the per-day crossings and phase lengths, the
   per-threshold "was it reached" test, and the cached year profile.
3. `panels/solarFiguresContent.ts` — the twilight rows, the "does not occur" statements, the
   threshold-definition statement, and the year's longest and shortest day.
4. `panels/SolarDayLengthPanel.tsx` — the canvas chart, the selected-date marker, the latitude
   and hemisphere caption, and the text description for assistive technology.
5. `panels/panelContracts.ts`, `viewer/extensions/builtin/solarAnalysisExtension.tsx` — the
   panel's type key, its data schema, its registration and its opening.

**Tests, with the behaviour they cover**: the solver's convergence and its refusal to
approximate; each threshold's crossing against an independently published table; the phase
lengths; the polar branches per threshold; the year profile's extremes; the figures rows and
their wording; the chart's rendering and its accessibility.

### What could go wrong, and where it is caught

| Risk | Caught by |
|---|---|
| The threshold times drift from published tables | A test that compares the computed crossings with published values for known sites and dates (SC-001) — the same kind of check specs/063 used for rise/set. |
| A threshold that is never reached is shown as a wrong time | The per-threshold hour-angle bound, unit-tested at high latitude for both hemispheres (SC-004). |
| The year profile is recomputed on every tick | The cache is keyed by site and year, and a test asserts the profile object is the same instance across two reads. |
| The chart costs the panel its responsiveness | The chart panel is a separate live panel that does not re-render the figures; the figures gain only a handful of rows. |
| A DST day is drawn wrongly | The crossings come from the same solver and the same local-time conversion the figures use; a test covers a 23-hour and a 25-hour local day. |

## Re-check after Phase 1 design

The constitution check above was re-run against the finished design. Two points changed and both
still pass:

- **§2.VII Convention over Configuration** now reads "the new panel is a live panel registered
  through the extension's existing path" rather than "a new content kind" — the live panel is the
  convention the solar feature already follows for its two other panels, so the new panel adds no
  new mechanism.
- **§2.III Simplicity** improves: the shared content vocabulary, its version, the committed JSON
  Schema artifact and the server's model-facing schema are all left untouched, so the feature
  touches no file outside `features/solar/` and the one extension shim.

No new violation appeared in Phase 1. The gate still passes.
