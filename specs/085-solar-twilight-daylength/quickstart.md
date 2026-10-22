# Quickstart: Verifying Spec 085

**Feature**: `085-solar-twilight-daylength` | **Date**: 2026-10-23

How to run the feature and confirm each requirement holds. Every check below is mechanical: it
names what to do and what to see. A person checking these does not need to judge whether a number
is *right* — only whether it matches the value stated here.

---

## Prerequisites

- Frontend running: `dotnet run --launch-profile https` (the Vite dev server, or the built app
  served by ASP.NET Core). A browser refresh alone will not pick up a change.
- Type-checking uses `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp`. A bare
  `tsc --noEmit` checks nothing in this repo.
- The unit tests: `npx vitest run src/features/solar` from `src/AskLucy.Web/ClientApp`.

---

## Check 1 — the twilight times agree with a published almanac (SC-001)

**Where**: the Solar Analysis panel, on the selected date.

**What to do**: pick a site and date, and read the civil, nautical and astronomical rows.

**Expected**: the three times match the published twilight times for the same site and date, to
within a minute at latitudes inside 72° of the equator.

**Where to get the reference values**: the US Naval Observatory's year-long twilight table for
the site's coordinates and year, or an equivalent published almanac. Record the values used in
the test file, so the check is against a fixed published source rather than against a value
recomputed by the same code being tested.

**If it does not match**: the threshold is being solved against the wrong quantity — see research
D1 and D2. The reported altitude constant (−5.43° at a civil crossing) is the quickest tell.

---

## Check 2 — the altitude figure reads one constant at every threshold (SC-002)

**Where**: the Solar Analysis panel.

**What to do**: note the altitude figure at a civil crossing, then move to a different site and
read it again.

**Expected**: the same number both times.

| Threshold | The altitude figure reads |
|---|---|
| Civil (6°) | −5.43° |
| Nautical (12°) | −11.43° |
| Astronomical (18°) | −17.43° |

**If it differs by site**: the threshold is being solved on the apparent altitude instead of the
geometric one (research D2).

---

## Check 3 — a phase that does not occur is said in words (SC-004)

**Where**: the Solar Analysis panel, at a high latitude in midsummer.

**What to do**: select a site inside the Arctic Circle (Tromsø, 69.65 N) and a date in June.

**Expected**: the panel says the sun does not reach the relevant depth below the horizon on that
date, and shows no time for that phase. The phases that do occur are still reported. The panel's
existing statement about the sun not setting is still there, unchanged, in its own place.

**Also check**: the same site in December. The polar statement ("the sun does not rise") appears,
and the twilight crossings that do occur are still reported.

---

## Check 4 — the phase lengths account for the day (SC-003)

**Where**: the Solar Analysis panel.

**What to do**: read the three phase lengths and the day length.

**Expected**: each phase's length is the band it occupies — civil from the horizon to −6°,
nautical from −6° to −12°, astronomical from −12° to −18°. The three lengths plus the night
account for the whole day to within a minute.

---

## Check 5 — the chart's bands agree with the figures (SC-006)

**Where**: the day-length chart panel, beside the Solar Analysis panel.

**What to do**: pick a date, note its stated sunrise and sunset, then find that date's row in the
chart.

**Expected**: the chart's day band for that row begins and ends at the stated sunrise and
sunset. Repeat for a date in the site's summer and one in its winter.

---

## Check 6 — the chart widens with the season (SC-007)

**Where**: the day-length chart panel.

**Expected**: the daylight band is widest in the site's own summer and narrowest in its own
winter. For a northern site that is around June and December; for a southern site, the opposite.
The panel states the site's latitude and which hemisphere it lies in.

---

## Check 7 — the chart does not cost the panel its responsiveness (SC-009)

**Where**: the Solar Analysis panel with the chart open.

**What to do**: press Play, or drag the time slider across a whole day.

**Expected**: the time readout keeps pace and the scene keeps up, exactly as it did before the
chart existed. The chart itself does not change while the time changes.

---

## Check 8 — the chart is read-only (SC-010)

**Where**: the day-length chart panel.

**What to do**: click anywhere on the chart, at a band, at the marked date, and at the caption.

**Expected**: nothing changes. The time does not move, the site does not change, the selection
does not change, and the chart does not change.

---

## Check 9 — the panel's existing figures are untouched (SC-005)

**Where**: the Solar Analysis panel.

**Expected**: sunrise, sunset, day length, the polar statement, the time basis, the accuracy
statement, the design-stage statement and the assumed-heights statement are all present, with the
same wording and the same values as before this feature.

---

## Check 10 — the panel is accessible (SC-012)

**Where**: the chart panel.

**What to do**: run the panel's accessibility test, and check both themes.

**Expected**: the automated check passes. The chart carries a text description of what it shows.
The bands are distinguishable in light and dark themes without relying on colour alone.

---

## Automated checks

| Test file | What it covers |
|---|---|
| `src/features/solar/solar/crossingSolver.test.ts` | The solver converges, refuses to return an unconverged seed, and returns `null` where the sun grazes the horizon. |
| `src/features/solar/solar/twilight.test.ts` | Each threshold's crossings against published values; the phase lengths; the per-threshold "reached" test at high latitudes in both hemispheres; the year profile's extremes and its day count. |
| `src/features/solar/panels/SolarDayLengthPanel.test.tsx` | The chart renders every day of the year, marks the selected date, states the latitude and hemisphere, and changes nothing when clicked. |
| `src/features/solar/panels/solarFiguresContent.test.ts` | The twilight rows, the "does not occur" wording, the threshold-definition statement, and that the existing rows are unchanged. |

Run them with `npx vitest run src/features/solar` from `src/AskLucy.Web/ClientApp`.

---

## If something is wrong

The feature is frontend-only and self-contained, so a wrong result is in one of three places:

| Symptom | Where to look |
|---|---|
| The times disagree with the almanac | The threshold target in `solar/twilight.ts` — research D1 and D2. |
| A phase is shown that does not occur, or vice versa | The per-threshold hour-angle bound — research D6. |
| The chart disagrees with the figures | The two must read the same day-level computation — research D12. |
| The panel got slow | The year profile is being recomputed per tick — research D9. |
