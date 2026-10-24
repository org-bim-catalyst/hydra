# Research: Solar Hour Lines (Analemma Curves)

**Feature**: specs/086-solar-hour-lines | **Date**: 2026-10-24

Every decision below records what was chosen, why, and what was rejected, in the house style of
specs/052, specs/063 and specs/085. Where a decision rests on a measurement, the measurement is
stated with the machine it was taken on.

---

## D1 — The hour lines are fixed furniture, built by `buildFixedFurniture`

**Decision**: the curves are built inside `buildFixedFurniture` and returned on
`FixedFurnitureObjects` as `hourLines: THREE.Mesh[]`; `SolarScene` adds them to
`sunPathFixedGroup`.

**Rationale**: specs/052 FR-021/FR-022 split the dome by lifetime precisely so a date or
time-of-day change rebuilds nothing whose shape does not depend on it. An hour's year-wide curve
depends on latitude, longitude, year and the site's clock — not on the selected date or instant —
so it belongs on the fixed side. Placing it there also inherits the existing disposal scope, the
existing identity tests and the existing scene ordering for free.

**Alternatives considered**:

- *Build the curves in the dated path.* Rejected: they would be thrown away and rebuilt on every
  date change — the exact regression T038–T041 in specs/052 exist to prevent, and 8,760 position
  evaluations per date step.
- *A third group for the curves.* Rejected: a third lifecycle means a third rebuild key, a third
  disposal path and a third place a date change can go wrong, for no property the fixed group does
  not already provide.

---

## D2 — One sample per local clock hour per day; the day arcs' horizon test

**Decision**: for hour `H` (0–23) and each day of the year, compute
`solarPosition(fromLocalParts(localDate, H * 60, timeZoneId), latitude, longitude)` and keep the
sample when `altitudeDegrees >= 0`.

**Rationale**: `hour * 60` is the exact minute-of-day the hour marks already use in
`buildDatedPath`, so a mark and its curve share one conversion, one hour angle and one solar
position. `altitudeDegrees >= 0` is the same filter `sampleDayArcPoints` applies to the day arcs,
so "on the dome" means the same thing for every family of curves. `altitudeDegrees` is the
refracted (displayed) altitude — the quantity the dome is drawn from — not the geometric one.

**Alternatives considered**:

- *Sample every 6 minutes, like `sampleDayArcPoints`.* Rejected: that is 24 × 240 × 365 ≈ 2.1 M
  evaluations — two orders of magnitude over the stated budget — and the analemma is smooth enough
  that a daily sample traces it faithfully.
- *Sample at `altitudeDegrees >= 2`, the hour marks' visibility threshold.* Rejected: the marks
  hide themselves when the sun is barely up so the labels do not crowd; the curves are geometry,
  and clipping them at 2° would leave visible hangs above the horizon.

---

## D3 — The sampled year is the selected date's year, and the day count is that year's own

**Decision**: the year is the one already derived from `localDate` in `updateSunPath`; the loop
runs from 1 January to 31 December of that year, 365 or 366 days.

**Rationale**: the fixed furniture is already keyed on that year, and the hour marks are drawn for
a date within it, so a mark can only be on a curve if both use the same year. A hardcoded 365 would
silently drop 29 February from every leap year, moving the curve by one day's worth of analemma
drift.

**Alternatives considered**:

- *A fixed 365-day budget from 1 January.* Rejected: it is wrong in every leap year, and the cost
  of one extra day is one hour's worth of samples.

---

## D4 — Above-horizon samples are split into contiguous runs

**Decision**: walk the year in day order; a below-horizon sample closes the current run. Each run
of two or more samples becomes one tube through `buildTubeFromPoints`; a shorter run draws nothing.

**Rationale**: a single curve through the surviving samples would join the last above-horizon day
of one stretch to the first of the next — a straight chord across the missing days, which at the
dome's radius cuts through the interior and usually through the ground. This is not hypothetical:
a probe of the real algorithm over 2026 found **three runs** for 06:00 and 07:00 at Sydney and two
runs for 06:00 at London, 07:00 at Singapore, 18:00 at Quito, and 05:00 and 11:00 at 78°N.

**Alternatives considered**:

- *Drop the runs with a gap smaller than N days.* Rejected: a threshold invents a tolerance the
  physics does not have, and would either hide a real horizon gap or keep a visible hairline.
- *Draw the runs as one `LineSegments` with a draw-range per run.* Rejected: `THREE.Line` renders
  at ~1 px regardless of width (specs/052 research D17), which is why every arc in this dome is a
  tube.

---

## D5 — The time zone joins the fixed-furniture signature and the rebuild key

**Decision**: `buildFixedFurniture(latitude, longitude, year, timeZoneId, hourLineColor,
radiusMetres?)`, and `SolarScene`'s fixed key becomes `site|year|radius|timeZoneId`.

**Rationale**: commit a4a5b674 fixed the hour marks to the site's clock; the curves must use the
same basis or every mark east or west of Greenwich leaves its curve. The time zone is resolved
asynchronously and the dome can be drawn first against the UTC fallback, so the zone must be an
input to the build and part of the condition that decides a rebuild — otherwise the first
resolution of the zone would leave the curves drawn on the wrong clock with no rebuild to correct
them. The overlay already re-runs its dome effect when the site object changes, so the rebuild
happens exactly once.

**Alternatives considered**:

- *Keep the signature and read the zone from the scene.* Rejected: the scene would then hold a
  second copy of the site's clock, and the rebuild key and the builder could disagree — the exact
  drift specs/063's contract warns about by making the year a parameter rather than reading it off
  a date.

---

## D6 — The `Intl.DateTimeFormat` is memoised per zone

**Decision**: `timeZone.ts` keeps one `Intl.DateTimeFormat` per zone id and reuses it instead of
constructing one inside `formatParts` on every call.

**Rationale**: the feature calls `fromLocalParts` 8,760 times (once per hour per day), plus a
second or third pass inside its fixed-point loop. `formatParts` currently constructs a formatter
per call. Measured on the development machine, in Node (the same V8 the app runs):

| Workload | Time |
|---|---|
| 8,760 fresh formatters (one format call each) | **476 ms** |
| 8,760 format calls on one cached formatter | **35 ms** |

`fromLocalParts` needs at least two format calls per conversion in a fixed-offset zone, so the
difference is roughly 0.9–1.4 s of main-thread stall against 0.07–0.1 s — the difference between a
visible hitch when the dome appears and no hitch at all. The formatter's options and the zone are
the only inputs, and neither varies per call, so a `Map<string, Intl.DateTimeFormat>` is the whole
change.

**Alternatives considered**:

- *Accept the stall.* Rejected: the feature would make opening the solar analysis demonstrably
  slower on every site, for no benefit.
- *Cache the offets per date/hour in the hour-line builder.* Rejected: the builders would have to
  duplicate `fromLocalParts`'s DST-aware convergence, which is precisely the code FR-009 says must
  not be duplicated.
- *Construct one formatter per build and pass it down.* Rejected: `fromLocalParts`'s public
  signature is the contract the marks already use, and the memo helps `toLocalParts` and the panel
  conversions too, not just this feature.

**Scope note**: this is a time-conversion helper, not the solar calculation. No result changes;
the existing `timeZone.test.ts` covers the conversion and stays green.

---

## D7 — The line colour comes from the MUI theme, applied in place on a change

**Decision**: the overlay reads `theme.palette.text.secondary` and passes it down
`updateSunPath(..., hourLineColorCss)`; `SolarScene` converts it with `THREE.Color` once and hands
the hex to `buildFixedFurniture`. When only the colour changes, the existing meshes' materials are
re-tinted and the method reports a rebuild (so the caller invalidates); the geometry is not rebuilt.

**Rationale**: `text.secondary` is a solid hex in this palette (`#524E46` light, `#9A9587` dark),
so it survives a `THREE.Color` parse, and it is already the theme's dim-ink token — "furniture you
read past" is exactly its intent. A neutral dim grey also cannot be confused with the chosen day's
red-orange, the amber summer extreme, the blue winter extreme or the amber lattice. Passing it from
React is the only route that keeps the requirement honest: the scene is constructed in the
extension's `start()`, outside the React tree, so there is no `useTheme` there, and this theme is
not created with `cssVariables`, so there is no CSS custom property to read either.

**Alternatives considered**:

- *A hardcoded grey in `sunPathCurve.ts`.* Rejected by spec FR-003 and constitution §7. It would
  also be the one colour in the dome that ignored the theme.
- *Read the computed CSS variable from `document`.* Rejected: this theme is a plain `createTheme`
  with no `cssVariables` option, so MUI emits classes, not custom properties; there is nothing
  stable to read.
- *Rebuild the furniture on a theme change.* Rejected: an 8,760-sample rebuild for a colour is
  precisely the kind of work this feature is built to avoid.
- *A third `setHourLineColor` setter plus a default colour.* Rejected: a default would be a
  hardcoded literal, and a required callback before the first build makes the draw order a hidden
  contract. An explicit argument has neither problem.

---

## D8 — Thinner and dimmer than the monthly lattice

**Decision**: tube radius `0.15`, opacity `0.5`, against the monthly lattice's `0.3` / `0.4` and
the chosen day's `0.7` / `1.0`.

**Rationale**: the hour curves are the densest family on the dome — up to 24 of them, and near the
equator many are large loops. They have to read as the background texture the marks sit on, not
compete with the chosen day, the two extremes or the monthly lattice. Half the lattice's radius
and a little more opacity keeps them visible at the dome's edge without clumping.

**Alternatives considered**:

- *The lattice's `0.3`.* Rejected: measured against a 120 m dome, 24 curves at lattice width read
  as a second lattice and flatten the hierarchy the spec requires.
- *Below `0.1`.* Rejected: at typical viewing distance that is sub-pixel, which is the same failure
  as `THREE.Line` — the tube must be thick enough to render as a tube.

---

## D9 — No new labels, and `userData.localHour` for identification

**Decision**: each run mesh carries `userData.localHour = hour`. No text, legend or panel entry is
added.

**Rationale**: the existing hour-number marks already label the curves they sit on; a second label
system would be redundant and would need i18n strings the feature does not otherwise have. The
`userData` tag is the same pattern the hour marks use (`sprite.userData.localHour`) and is what
lets a test name "the 12:00 curve" without relying on array order.

**Alternatives considered**:

- *Name the meshes.* Rejected: `THREE.Object3D.name` is a string allocation per mesh for a use
  only tests and debuggers need; the existing marks already set the `userData` precedent.

---

## D10 — Disposal is inherited, not added

**Decision**: the curve meshes are added to `sunPathFixedGroup`; the existing
`disposeGroupContents` deep traversal disposes their geometry and materials.

**Rationale**: specs/052 FR-023/SC-010 require repeated open/close and site changes to leave
nothing accumulated. The traversal already disposes geometry, materials and textures on every
child, so putting the curves in the group it covers is the whole implementation. A separate scope
would be a second thing to forget.

**Alternatives considered**:

- *Share one material across all runs.* Rejected as premature: it would save a handful of materials
  and make the colour re-tint simpler, but the existing arc builders each own their material, and
  the deep disposal already handles them. Revisit only if the material count becomes measurable.

---

## D11 — Test fixtures come from a real probe of the algorithm

**Decision**: pin the following, all measured with the real `solarPosition` + `fromLocalParts` over
2026:

| Site (lat, lng, zone) | Fact |
|---|---|
| Sydney (−33.87, 151.21, `Australia/Sydney`) | 06:00 and 07:00 each produce **three** runs — the real-data split proof |
| Singapore (1.35, 103.82, `Asia/Singapore`) | 07:00 and 19:00 each produce two runs |
| London (51.5, −0.1, `Europe/London`) | 06:00 and 07:00 each produce two runs |
| Quito (−0.18, −78.47, `America/Guayaquil`) | 18:00 produces two runs |
| 78°N (78, 15.6, `Arctic/Longyearbyen`) | 05:00 and 11:00 each produce two runs |
| Dubai (25.2, 55.3, `Asia/Dubai`) | 00:00 produces **zero** runs (never above the horizon); 12:00 exists |
| Dubai, London, Sydney | the 12:00 mark's position equals a 12:00-curve sample |

**Rationale**: a synthetic splitter test proves the rule, but a real site proves the rule is
reached by the shipped data. Sydney is the strongest case (three runs at one hour), and Dubai's
midnight hour is the cleanest "never sees the sun" case because it is certain rather than marginal
— midnight sun does not occur at 25°. These numbers are properties of the published NOAA
algorithm, not of this implementation; if they ever fail, either the algorithm or the fixture has
changed and both deserve a look.

**Alternatives considered**:

- *Only synthetic fixtures.* Rejected: they would leave the split branch untested against real
  output, and the risk being guarded against is a real chord through a real gap.
- *Assert the exact hour sets at each site.* Rejected: brittle against the algorithm's small
  refraction and tolerance numbers; assert the properties that matter (a known three-run hour, a
  known never-up hour, a known alignment).
