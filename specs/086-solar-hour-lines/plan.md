# Implementation Plan: Solar Hour Lines (Analemma Curves)

**Branch**: `086-solar-hour-lines` | **Date**: 2026-10-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/086-solar-hour-lines/spec.md`

## Summary

For each local clock hour 0–23, draw the curve the sun traces at that hour on every day of the
selected year — that hour's analemma — as a thin, dim tube on the existing sun-path dome.

The curves are **fixed furniture**: they are built once per (site, year, time zone) and never
rebuilt by a date or time-of-day change. Each hour's year is sampled once per day through the
same `fromLocalParts` conversion the hour-number marks use, so a mark always lands on its own
curve; the above-horizon samples are split into contiguous runs so no tube is drawn across a
below-horizon stretch. Hours that never see the sun in the year (midnight at mid-latitudes) draw
nothing.

No backend work, no new dependency, no new panel, no new user-facing text.

## Technical Context

**Language/Version**: TypeScript 5.x (React 19, three 0.185) on the .NET 10 backend; this feature
is frontend-only
**Primary Dependencies**: none added — `three` (already present), the feature's own
`solarPosition` and `timeZone` modules, and the MUI theme
**Storage**: none — computed in the browser, session-scoped, never persisted
**Testing**: vitest (`src/features/solar/scene/sunPathCurve.test.ts` plus new cases)
**Target Platform**: Web (the viewer's WebGL drawing space)
**Project Type**: web-service + single-page frontend; **frontend-only** change
**Performance Goals**: the curves cost one build per site/year (~8,760 position evaluations) and
nothing per frame or per tick; scrubbing and playback must show no measurable regression (spec
FR-010, SC-004)
**Constraints**: fixed furniture only (specs/052 FR-021/FR-022); local clock hours via
`fromLocalParts`; colours from the MUI theme, never a new literal; no change to the solar
calculation, shadows, figures panel, day-length chart or backend
**Scale/Scope**: 2 source files changed + 1 small helper in `timeZone.ts`; ~3 test/doc files;
0 backend files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicable | Assessment |
|---|---|---|
| §2.I Clean Architecture | No | Frontend-only, entirely inside `features/solar/`. No new cross-layer edge; the backend is untouched. |
| §2.II SOLID | Yes | The new sampling/splitting/building functions each have one reason to change and live beside the existing arc builders in `sunPathCurve.ts`; `SolarScene` keeps owning the scene graph and rebuild keys. |
| §2.III Simplicity / YAGNI | Yes | No new dependency, no new abstraction, no new module. The existing `buildTubeFromPoints`, `sphericalToVec`, `fromLocalParts` and `solarPosition` are reused as-is. The only new helper outside the scene is a formatter memo that removes a measured build stall (research D6). |
| §2.VI Separation of Concerns | Yes | Sampling and run-splitting stay in `sunPathCurve.ts`; the theme colour is resolved in React and passed down, not read from the DOM; no new user-facing strings. |
| §2.VII Convention over Configuration | Yes | The curves join the existing fixed-furniture builder and the existing fixed group; no new mechanism, no new panel, no new content kind. |
| §2.VIII No Silent Failures | Yes | An hour with no above-horizon samples is a fact about the site, not an error, and draws nothing by design (FR-006). Nothing in this feature can throw silently: the build is synchronous and pure, and any exception reaches the extension's existing failure surface through the caller's effect. |
| §3 Architecture Rules | No | Frontend-only. |
| §7 UI Principles | Yes | The line colour is `theme.palette.text.secondary`, passed from the overlay (which already renders under `ThemeProvider`), so light and dark themes both apply and no new literal is introduced. No new text, so no i18n surface. |
| §10 Testing Standards | Yes | New behaviour is covered by unit tests in the same change: horizon filter, run splitting, never-up hours, marks-on-curves, fixed-furniture identity and the colour re-tint. |
| §13 Documentation | Yes | This plan, the contract, the quickstart and the solar README's module map ship with the code; specs/063's scene contract gains a pointer to the amendment. |
| §15 Performance | Yes | The one-off build cost is measured and reduced before shipping (D6); the per-tick path is unchanged and asserted by the existing identity tests. |

**Gate result**: pass. No complexity tracking is required.

## Project Structure

### Documentation (this feature)

```text
specs/086-solar-hour-lines/
├── spec.md              # this feature's specification
├── plan.md              # This file
├── research.md          # Phase 0 output — 11 decisions with alternatives
├── data-model.md        # Phase 1 output — the entities the feature introduces
├── quickstart.md        # Phase 1 output — how to verify it end to end
├── contracts/
│   └── solar-hour-lines.md   # the builder/scene contract + the 063 amendment
└── tasks.md             # Phase 2 output (/speckit-tasks) — NOT created here
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/solar/
├── solar/
│   └── timeZone.ts            # MODIFIED — memoise the Intl.DateTimeFormat per zone (D6)
├── scene/
│   ├── sunPathCurve.ts        # MODIFIED — buildHourLines / sampleHourLineRuns / the split rule;
│   │                          #   buildFixedFurniture gains the time zone and the line colour
│   ├── sunPathCurve.test.ts   # MODIFIED — the new behaviour's tests
│   └── SolarScene.ts          # MODIFIED — passes the time zone + colour, keys the fixed rebuild
│                              #   on the time zone, re-tints in place on a colour change
└── components/
    └── SolarAnalysisOverlay.tsx  # MODIFIED — passes theme.palette.text.secondary

specs/063-solar-accuracy-performance/contracts/
└── solar-scene.md             # MODIFIED — a pointer to this feature's contract

src/AskLucy.Web/ClientApp/src/features/solar/README.md  # MODIFIED — module map entry
```

**Structure Decision**: everything lives in the existing `features/solar/` module. The only change
outside it is one documentation pointer, because the dome's binding contract is specs/063's. No
new top-level directory, no new project, no new dependency.

## Key design decisions

### The curves are fixed furniture, built by the fixed builder

`FixedFurnitureObjects` gains `hourLines: THREE.Mesh[]`; `buildFixedFurniture` builds them beside
the monthly lattice and the scene adds them to `sunPathFixedGroup`. This is what makes the
existing "a date change rebuilds only the dated group" tests cover the curves for free.

### The time zone is a fixed-furniture input and part of the rebuild key

`buildFixedFurniture` gains a required `timeZoneId`, and `SolarScene`'s `fixedKey` becomes
`site|year|radius|timeZoneId`. The overlay already receives the time zone and re-runs on the site
object, so the one rebuild that matters — fallback UTC resolving to the site's real zone — happens
exactly once, on the site's clock.

### The colour travels from React to the scene, and a change re-tints rather than rebuilds

`updateSunPath` gains the line colour as a CSS string (the overlay passes
`theme.palette.text.secondary`); `SolarScene` converts it once to a three.js hex and hands it to
`buildFixedFurniture`. If only the colour changed, the existing meshes' materials are re-tinted in
place and `updateSunPath` reports a rebuild, so the caller invalidates — no 8,760-sample rebuild
for a theme toggle.

### The build cost is paid once, and the known stall is removed first

24 × 365 samples each run `fromLocalParts`, which today constructs a new `Intl.DateTimeFormat` on
every call. Measured on this machine: 8,760 calls with a fresh formatter cost **476 ms**, against
**35 ms** with one cached per zone — before the second conversion iteration `fromLocalParts` needs.
The formatter is memoised per zone in `timeZone.ts` (research D6). This touches the time conversion
the whole solar feature shares; it changes no result, only how many formatter objects are built.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. No violation needed a justification.

## Phase 0 — research

Complete: [research.md](research.md). Eleven decisions, each with the alternatives considered.
The load-bearing ones:

| Decision | Consequence for the code |
|---|---|
| D1 | Hour lines are fixed furniture, returned by `buildFixedFurniture`. |
| D2 | One sample per local clock hour per day; horizon test is the same `altitudeDegrees >= 0` the day arcs use. |
| D3 | The year is the selected year; the day count is the year's own (365 or 366). |
| D4 | Above-horizon samples are split into contiguous runs; a run of fewer than two points draws nothing. |
| D5 | The time zone joins the fixed-furniture signature and the rebuild key. |
| D6 | The `Intl.DateTimeFormat` is memoised per zone in `timeZone.ts` (measured 476 ms → 35 ms). |
| D7 | The colour is `theme.palette.text.secondary`, converted once in `SolarScene`, re-tinted in place. |
| D8 | Tube radius 0.15, opacity 0.5 — thinner and dimmer than the monthly lattice (0.3/0.4). |
| D9 | No new labels; `userData.localHour` identifies each run's hour for tests and debugging. |
| D10 | Disposal is inherited from the existing fixed group. |
| D11 | Test fixtures: Sydney 06:00 gives three runs; Dubai 00:00 gives none; the 12:00 curve is pinned to the 12:00 mark at three sites. |

## Phase 1 — design

Complete: [data-model.md](data-model.md), [contracts/solar-hour-lines.md](contracts/solar-hour-lines.md),
[quickstart.md](quickstart.md).

### The shape of the work

**Backend**: none.

**Frontend, in dependency order**:

1. `solar/timeZone.ts` — memoise the formatter per zone id. No signature or result change.
2. `scene/sunPathCurve.ts` — `sampleHourLineRuns` (per hour and day, via `fromLocalParts`), the
   exported `splitAboveHorizonRuns` rule, `buildHourLines`, and the extended
   `buildFixedFurniture(latitude, longitude, year, timeZoneId, hourLineColor, radius?)`.
3. `scene/SolarScene.ts` — the extended `updateSunPath(..., timeZoneId, hourLineColorCss)`, the
   time zone in the fixed key, the `hourLines` array added to the fixed group, and the in-place
   re-tint when only the colour changes.
4. `components/SolarAnalysisOverlay.tsx` — `useTheme()`, pass `theme.palette.text.secondary` and
   add it to the effect's dependencies.
5. `scene/sunPathCurve.test.ts` — the new cases (below).

**Tests, with the behaviour they cover**:

| Test | Covers |
|---|---|
| Every point of every real-site run is at or above the horizon (Dubai, London, Sydney, Singapore, 78°N) | FR-005, SC-003 |
| Sydney 06:00 (and 07:00) yields three runs; no run spans a below-horizon sample | FR-004, SC-003 |
| The splitter unit test: `[up, up, down, up]` → two runs | FR-004 |
| Dubai 00:00 draws no curve, 12:00 draws one | FR-006 |
| The 12:00 mark's position equals a sample of the 12:00 curve at Dubai, London and Sydney | FR-009, SC-006 |
| Fixed-group identities survive a year of date changes and a day of time changes, with the hour lines present | FR-007, SC-002 |
| A time-zone change (same site and year) rebuilds the fixed furniture once | FR-008 |
| A colour-only change re-tints the existing meshes without replacing them | FR-003 |
| `buildFixedFurniture` and every run mesh carry the theme colour passed in | FR-003 |

### What could go wrong, and where it is caught

| Risk | Caught by |
|---|---|
| A mark floats off its curve because one side used UTC | Both sides call `fromLocalParts`; the 12:00 alignment test fails if either drifts. |
| The curves rebuild on a date change and stall scrubbing | The fixed-key and group-identity tests, plus the time zone being in the key. |
| A tube is drawn across the winter gap | The Sydney 06:00 three-run test and the splitter unit test. |
| The build stalls the main thread for a second | The formatter memo (D6); a test asserts the line set is built once, not that it is fast. |
| A theme toggle rebuilds 8,760 samples | The colour-only re-tint test. |
| The geometry leaks across open/close cycles | The existing disposal tests, now covering the hour-line meshes in the fixed group. |

## Re-check after Phase 1 design

The constitution check above was re-run against the finished design. One point changed:

- **§2.III Simplicity** is still satisfied but now carries one small file outside the scene:
  `timeZone.ts` gains a formatter memo. It is six lines, changes no result, and removes a measured
  ~450 ms stall at the exact moment the dome is built; the alternative — accepting the stall — is a
  worse user experience for the same feature. It is recorded as research D6 rather than hidden.

No new violation appeared. The gate still passes.
