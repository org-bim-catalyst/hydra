# Quickstart: Verifying Spec 086

**Feature**: `086-solar-hour-lines` | **Date**: 2026-10-24

How to run the feature and confirm each requirement holds. Every check below is mechanical: it
names what to do and what to see, or the command to run and the value it must print.

---

## Prerequisites

- From `src/AskLucy.Web/ClientApp`:
  - Type-check: `npx tsc -b --noEmit` (a bare `tsc --noEmit` checks nothing in this repo — the
    root tsconfig uses project references).
  - Tests: `npx vitest run src/features/solar`
  - Lint: `npm run lint`
- To see it: run the app, open a site, activate **Solar Analysis** from the viewer toolbar. The
  dome draws around the site building.

---

## Check 1 — the curves are on the dome and read as background (FR-001 … FR-003, SC-001)

**What to do**: open the analysis at a mid-latitude site (Dubai is the default fixture) and look at
the dome.

**Expected**: a family of thin, dim grey curves, one per hour that sees the sun at some point in the
year, each lying on the dome's shell. They are visibly thinner and dimmer than the amber monthly
lattice, the red-orange chosen-day arc, the amber summer arc and the blue winter arc.

**Not expected**: any label, legend or panel content for the curves. The hour numbers already on
the dome are the only labels.

---

## Check 2 — every hour mark sits on its own curve (FR-009, SC-006)

**What to do**: pick a mark on the chosen day's arc — the "12" is the easiest to find — and follow
the curve through it.

**Expected**: the mark is exactly on a curve, not beside one. Repeat for a morning hour and an
evening hour.

**Automated**: `sunPathCurve.test.ts` asserts, at Dubai, London and Sydney, that the 12:00 mark's
position equals a sample of the 12:00 curve to within `1e-6`.

---

## Check 3 — scrubbing and playback rebuild nothing (FR-007, SC-002)

**What to do**: drag the time slider across a full day, then step dates across a year, then press
play.

**Expected**: the hour curves never move, flicker or disappear. Only the chosen-day arc, the hour
marks and the sun marker change.

**Automated**: the existing fixed-group identity tests now include the hour lines; a new case
asserts the fixed group still contains hour-line meshes after a year of date changes and a day of
time changes.

---

## Check 4 — no tube is drawn through the ground (FR-004, FR-005, SC-003)

**What to do**: set a site with a known horizon gap. Sydney, 2026, is the pinned case: at local
**06:00** the sun is up in **three** separate stretches of the year.

**Expected**: three separate curves for 06:00, each stopping at the horizon. No tube crosses the
gap, and no tube dips below the ground plane.

**Automated**: a real-site test asserts three runs at Sydney 06:00; a pure splitter test asserts
`[up, up, down, up]` → two runs; every real-site test asserts every point is at or above the
horizon.

---

## Check 5 — hours that never see the sun draw nothing (FR-006)

**What to do**: at Dubai, look for a curve at local **00:00**.

**Expected**: none. Midnight sun does not happen at 25°, so the midnight hour never has the sun up
and draws no curve. The 12:00 curve is present.

**Automated**: `buildHourLines` at Dubai yields no mesh with `userData.localHour === 0` and at
least one with `userData.localHour === 12`.

---

## Check 6 — the theme is the source of the colour (FR-003, SC-005)

**What to do**: toggle the app from light to dark theme with the analysis open.

**Expected**: the hour curves change to the other theme's dim ink colour immediately, and the dome
is **not** rebuilt (nothing else on the dome moves or flashes).

**Automated**: a test changes only the colour passed to `updateSunPath` and asserts the same mesh
objects are returned with a different material colour, and that `updateSunPath` reported a rebuild
so the caller invalidates.

---

## Check 7 — the build cost is paid once (SC-004)

**What to do**: open a new site (or change the year), then scrub and play.

**Expected**: the dome appears with the curves in well under a second at a normal site; scrubbing
and playback show no hitch attributable to the curves.

**Measured basis** (research D6): 8,760 conversions with a memoised formatter cost ~35 ms of
formatting, against ~476 ms when a formatter is constructed per call. The formatter memo in
`timeZone.ts` is what keeps the build below the perception threshold.

---

## Check 8 — nothing else changed (FR-013)

**Commands**:

```
npx vitest run src/features/solar
npx tsc -b --noEmit
npm run lint
```

**Expected**: all green. In particular, the shadows, the figures panel, the day-length chart and
the telescope-free backend are untouched: `git diff --stat` must show no backend file and no file
under `features/solar/solar/` other than `timeZone.ts`.

---

## If a check fails

| Symptom | Likely cause |
|---|---|
| The "12" mark sits beside its curve | One side is using UTC and the other the site's zone — check the `fromLocalParts` minute-of-day on both paths. |
| A straight line crosses the dome interior | The run split was not applied, or a run was merged after a below-horizon sample. |
| Curves rebuild while scrubbing | The curves were built in the dated path, or the fixed key lost a dependency. |
| The curves stay on UTC after the site loads | The time zone is not in the fixed rebuild key. |
| Curves are a hardcoded grey | The colour did not come from `theme.palette.text.secondary`. |
| The dome takes about a second to appear | The formatter memo is not in place. |
