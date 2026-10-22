# Data Model: Solar Twilight Times & Year Day-Length Chart

**Feature**: specs/085-solar-twilight-daylength | **Date**: 2026-10-23

Everything here is computed in the browser from the site's latitude, longitude and the selected
date. Nothing in this feature is persisted, fetched from a server, or sent anywhere: the solar
module already computes the sun's position in the client (specs/052 research D1), and a twilight
crossing is the same computation with a different target altitude.

---

## Entity: Twilight Threshold

One of the three depths at which a twilight phase begins or ends.

| Field | Type | Notes |
|---|---|---|
| `degreesBelowHorizon` | `number` | 6, 12 or 18. |
| `phase` | `'civil' \| 'nautical' \| 'astronomical'` | The phase this threshold's crossing marks. |
| `label` | `string` | The user-facing name, from `copy.ts`. |

**Definition rule**: the threshold is the **geometric altitude of the sun's centre**. The centre
is used because every published almanac defines twilight that way (research D1); the geometric
altitude is used because that is the quantity the published tables are computed from.

**Validation**: the three thresholds are a fixed set. There is no per-site or per-user
configuration, and none is offered — a threshold the user could change would no longer mean what
the almanacs mean, and the panel's whole value is that the figure can be checked against one.

---

## Entity: Threshold Crossing

The instant at which the sun's centre crosses a threshold, on the way down or on the way up.

| Field | Type | Notes |
|---|---|---|
| `threshold` | `TwilightThreshold` | Which threshold was crossed. |
| `direction` | `'descending' \| 'ascending'` | Down toward night, or up toward day. |
| `instantUtc` | `Date` | The solved instant, to the millisecond. |
| `localMinuteOfDay` | `number` | The same instant in the site's local time. |
| `reached` | `boolean` | `false` when the sun never reaches that threshold on that date. |
| `undetermined` | `boolean` | `true` when the solver did not converge, so the instant is unknown. |

**Relationships**: a crossing belongs to a site and a date. A day has at most two crossings per
threshold (one descending, one ascending); a threshold that is not reached has neither, and a
grazing threshold has one.

**State**: `reached` and `undetermined` are distinct on purpose. A threshold that was never
reached is a fact about the day; a threshold whose crossing could not be solved is a failure of
the computation. The panel says different things for each (research D6), and the second must
never be presented as the first.

**Validation**: a crossing's `localMinuteOfDay` is derived from `instantUtc` by the site's time
zone, the same conversion the figures panel already uses. A crossing is never reported from a
seed the solver did not converge to — an unconverged answer is `undetermined`, not an estimate.

---

## Entity: Twilight Phase Length

The duration of the band a phase occupies, on the evening side.

| Field | Type | Notes |
|---|---|---|
| `phase` | `'civil' \| 'nautical' \| 'astronomical'` | Which phase. |
| `minutes` | `number \| null` | The duration; `null` when the phase does not occur. |
| `fromThreshold` | `TwilightThreshold \| 'horizon'` | The phase's upper boundary. |
| `toThreshold` | `TwilightThreshold` | The phase's lower boundary. |

**Definition rule**: civil twilight is the time from the horizon to −6°, nautical from −6° to
−12°, astronomical from −12° to −18° (research D5). The evening side is used because that is the
number people mean when they ask how long twilight lasts, and because it is the width of the band
on the chart.

**Validation**: a phase's length is `null` exactly when its lower threshold was not reached. It is
never zero, and never negative: the solver's crossings are ordered by construction, and a
regression that produced one would be a bug in the solver, not in the data.

---

## Entity: Day Twilight Summary

Everything the Solar Analysis panel reports about the selected date.

| Field | Type | Notes |
|---|---|---|
| `crossings` | `Threshold Crossing[]` | Up to six: two per threshold. |
| `lengths` | `Twilight Phase Length[]` | One per phase. |
| `sunriseUtc` / `sunsetUtc` | `Date \| null` | From the existing `daySummary`, unchanged. |
| `polarCondition` | `'none' \| 'midnight-sun' \| 'polar-night'` | From the existing `daySummary`, unchanged. |

**Relationships**: the summary is derived from the crossings and the existing day summary; it
holds no state of its own. The figures panel and the chart both read it, so the two can never
disagree (research D12).

**Validation**: the summary is recomputed whenever the selected date or the site changes, and
cached per (site, date) so a playback tick — which changes the time of day but not the date —
does not re-solve it.

---

## Entity: Day Phase Sequence

The ordered phases of one day, which is what the chart draws.

| Field | Type | Notes |
|---|---|---|
| `date` | `string` | The site's local calendar date, `YYYY-MM-DD`. |
| `segments` | `Phase Segment[]` | The day's phases in order, covering the whole local day. |
| `daylightMinutes` | `number` | The length of the daylight segment. |

### Entity: Phase Segment

| Field | Type | Notes |
|---|---|---|
| `phase` | `'night' \| 'astronomical' \| 'nautical' \| 'civil' \| 'day'` | Which phase. |
| `startMinute` | `number` | Local minutes from the start of the local day. |
| `endMinute` | `number` | Local minutes from the start of the local day. |
| `colorToken` | `string` | The theme colour resolved for the phase. |

**Validation**: the segments of a day are contiguous and cover the whole local day, with no gap
and no overlap. A day with no sunrise is one `night` segment; a day with no sunset is one `day`
segment (research D12).

**Daylight-saving note**: the local day is 23, 24 or 25 hours long. The segments are measured in
local minutes from the start of the local day, so a 25-hour day is longer in minutes than a
24-hour day and the chart's hour axis is the day's own length. A repeated local hour occurs twice
in the sequence and a missing one not at all, and the segments are contiguous either way.

---

## Entity: Year Profile

Every day of the calendar year, for one site.

| Field | Type | Notes |
|---|---|---|
| `year` | `number` | The calendar year. |
| `latitude` / `longitude` | `number` | The site. |
| `timeZoneId` | `string \| null` | The site's time zone, as the panel already resolves it. |
| `days` | `Day Phase Sequence[]` | One per day of the year, in calendar order. |
| `longestDay` | `{ date: string; minutes: number }` | The year's longest daylight. |
| `shortestDay` | `{ date: string; minutes: number }` | The year's shortest daylight. |
| `hemisphere` | `'northern' \| 'southern'` | From the latitude's sign. |

**Relationships**: the profile is built from the same day-level computation the figures use, one
day at a time, so the chart's bands and the panel's figures cannot disagree (research D12).

**Validation**: the profile holds one entry per day of the year — 365, or 366 in a leap year. The
longest and shortest day are computed from the same `daylightMinutes` the chart draws. A site at
the equator has a nearly constant day length; a site inside the polar circles has days of
continuous daylight and continuous night, and both appear as single segments (research D12).

**Caching**: the profile is computed once per (site, year) and reused. It is *not* recomputed
when the time of day changes, and not when the selected date changes within the same year
(research D9).

---

## Entity: Chart Panel State

The live panel's own state — the only state the feature introduces that is not derived.

| Field | Type | Notes |
|---|---|---|
| `profile` | `Year Profile \| null` | The cached profile, `null` while it is being computed. |
| `selectedDate` | `string` | The day marked on the chart. |

**Validation**: the panel is read-only. It writes no time, no site and no selection (spec
FR-015); the only thing it holds is the profile it was given and the date to mark.

---

## What is *not* in this model

- **No server entity.** There is no table, no row, no column. The feature adds nothing to the
  database and nothing to the API.
- **No user preference.** Which thresholds are shown, and the chart's presence, are not
  settings: the thresholds are the published ones, and the chart is part of the analysis.
- **No persisted state.** A reload recomputes the profile. That is a few tens of milliseconds,
  once (research D9).
