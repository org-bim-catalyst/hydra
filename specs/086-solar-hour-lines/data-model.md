# Data Model: Solar Hour Lines (Analemma Curves)

**Feature**: specs/086-solar-hour-lines | **Date**: 2026-10-24

Everything here is computed in the browser from the site's latitude, longitude, the selected year
and the site's time zone. Nothing is persisted, fetched, stored or sent anywhere: an hour line is
a second reading of the same sun-position function the dome already draws with.

---

## Entity: Hour Line

One local clock hour's year-wide curve — that hour's analemma.

| Field | Type | Notes |
|---|---|---|
| `localHour` | `number` | 0–23, on the **site's** clock (FR-009). Also carried on each run mesh as `userData.localHour`. |
| `runs` | `HourLineRun[]` | The contiguous above-horizon stretches, in day order. Empty when the hour never sees the sun in the year (FR-006). |

**Identity**: the local hour. Two calls with the same site, year and time zone produce the same
curve; a different time zone produces a different curve even at the same coordinates, because the
hour means a different instant (FR-008).

**Validation**: the set of hours is fixed at 0–23; there is no user configuration, no threshold
setting and no opt-in. An hour with no runs is a valid, complete hour line — it simply draws
nothing.

---

## Entity: Hour Line Sample

One reading of the sun at a given local hour on a given day.

| Field | Type | Notes |
|---|---|---|
| `dayIndex` | `number` | 0-based day of the year, 0 = 1 January. Range 0 … 364/365. |
| `localDate` | `string` | `YYYY-MM-DD` for that day, in the site's calendar. |
| `instantUtc` | `Date` | `fromLocalParts(localDate, localHour * 60, timeZoneId)` — the same conversion the hour marks use. |
| `position` | `SolarPositionResult` | `solarPosition(instantUtc, latitude, longitude)`; only `altitudeDegrees` and `azimuthDegrees` are used. |
| `aboveHorizon` | `boolean` | `position.altitudeDegrees >= 0` — the day arcs' own filter. |
| `vector` | `Vector3` | `sphericalToVec(azimuth, altitude, radiusMetres)`; present only when `aboveHorizon`. |

**Validation**: exactly one sample per (hour, day). A local clock hour that does not exist on a
DST transition date still resolves to exactly one instant through `fromLocalParts`'s fixed-point
convergence, so the count never changes (FR-014); one that occurs twice also resolves to one
instant, by the same rule.

---

## Entity: Hour Line Run

A maximal contiguous stretch of above-horizon samples for one hour.

| Field | Type | Notes |
|---|---|---|
| `localHour` | `number` | The hour this run belongs to. |
| `points` | `Vector3[]` | The run's dome positions, in day order. |
| `mesh` | `Mesh` | The tube built from these points, or absent when `points.length < 2`. |

**Validation rules**:

- Every point is at or above the horizon (FR-005): `sphericalToVec` with a non-negative altitude
  puts every point at `z >= 0`.
- No two consecutive samples in a run are separated by a below-horizon day (FR-004). A
  below-horizon sample always closes the current run, so a run is contiguous by construction.
- A run of fewer than two points draws no mesh (FR-004); a single point has no length to tube.
- Runs are drawn at `radiusMetres`, the dome's current radius, so a resized dome redraws them with
  everything else (edge case in the spec).

**Relationships**: an hour line owns 0 … n runs; a run belongs to exactly one hour.

---

## Entity: Fixed Furniture (extended)

The dome's site-and-year-scoped geometry, from specs/063's `FixedFurnitureObjects`.

| Field | Type | Notes |
|---|---|---|
| `dial` | `Mesh` | Unchanged. |
| `mountPost` | `Group` | Unchanged. |
| `shell` | `Mesh` | Unchanged; still draws last (`renderOrder`). |
| `monthlyArcs` | `Mesh[]` | Unchanged. |
| `hourLines` | `Mesh[]` | **New.** Every run mesh for every hour, in hour order. |

**Dependencies**: latitude, longitude, year, time zone, radius, and the line colour. Only the
first four and the radius decide whether a rebuild is needed; the colour is applied in place
(FR-007, FR-008, research D7).

**Lifetime**: built once with the rest of the fixed furniture, disposed with it. A date or
time-of-day change never touches it (FR-007).

---

## Entity: Hour Line Appearance

The presentational values the builder takes.

| Field | Type | Source | Notes |
|---|---|---|---|
| `color` | `number` (0xRRGGBB) | `theme.palette.text.secondary`, converted once in `SolarScene` | Never a literal in the scene (FR-003, constitution §7). |
| `tubeRadiusMetres` | `number` | constant `0.15` | Thinner than the monthly lattice's `0.3`. |
| `opacity` | `number` | constant `0.5` | Dimmer than the day arc's `1.0`; readable in both themes. |

**Validation**: the colour is supplied by the caller on every `updateSunPath`; there is no default
to fall back to, so no hardcoded colour can be introduced by omission.
