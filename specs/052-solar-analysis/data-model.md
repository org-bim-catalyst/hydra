# Phase 1 Data Model: Solar Analysis

Entities from the spec's Key Entities section, given concrete shape. Nothing here is persisted —
this feature adds no table and no migration (spec Out of Scope: corrections are session-scoped;
building footprints are fetched per request).

---

## Site

The location being analysed, plus the local time basis that applies there.

| Field | Type | Notes |
|---|---|---|
| `latitude` | `number` | −90…90. From `activeLocationStore`, not owned here. |
| `longitude` | `number` | −180…180. |
| `timeZoneId` | `string \| null` | IANA id from `tz-lookup` (research D2). `null` when it could not be determined. |
| `timeBasisLabel` | `string` | What is shown to the user — the zone id, or the explicit "UTC — time zone could not be determined" statement (FR-003). Never absent. |
| `siteKey` | `string` | `` `${lat.toFixed(6)},${lng.toFixed(6)}` `` — the correction-store key (research D11) and the stale-response guard key (research D10). |

**Rule**: `timeBasisLabel` is derived, never stored independently, so the stated basis and the basis
actually used cannot drift apart.

---

## Solar Position

Where the sun is for a site at one instant.

| Field | Type | Notes |
|---|---|---|
| `azimuthDegrees` | `number` | 0…360, clockwise from true north. |
| `altitudeDegrees` | `number` | −90…90. Negative means below the horizon. |
| `isAboveHorizon` | `boolean` | `altitudeDegrees > 0`. Drives FR-017 (no shadows when below). |

**Derived, never stored**: the ENU unit vector used to place the light —
`x = sin(az)·cos(alt)`, `y = cos(az)·cos(alt)`, `z = sin(alt)` — which is directly usable as a
Three.js position because specs/051's local frame maps onto the scene axes with no remapping.

---

## Day Summary

Sunrise, sunset and day length for a site and date.

| Field | Type | Notes |
|---|---|---|
| `sunriseUtc` | `Date \| null` | `null` when the sun does not rise that day. |
| `sunsetUtc` | `Date \| null` | `null` when the sun does not set that day. |
| `dayLengthMinutes` | `number \| null` | `null` in either null case above. |
| `polarCondition` | `'none' \| 'midnight-sun' \| 'polar-night'` | The plain statement FR-002 requires, as a value rather than an inference from two nulls. |

**Rule**: `polarCondition` is computed from `astronomy-engine`'s own null returns *plus* the sun's
altitude at local solar noon — altitude above horizon at noon with no rise/set means midnight sun;
below means polar night. Two nulls alone cannot distinguish them.

---

## Sun Path

The sun's track across the sky for a date, with the seasonal extremes.

| Field | Type | Notes |
|---|---|---|
| `chosenDay` | `SolarPosition[]` | Sampled across the day; below-horizon samples retained but flagged. |
| `summerExtreme` | `SolarPosition[]` | Summer solstice for this hemisphere. |
| `winterExtreme` | `SolarPosition[]` | Winter solstice for this hemisphere. |
| `hourMarks` | `{ localHour: number; position: SolarPosition }[]` | Marked along the chosen day (FR-006). |
| `currentPosition` | `SolarPosition` | The sun at the analysis moment (FR-007). |

**Rule**: the three tracks must be visually distinguishable (FR-006) — enforced by the renderer
using distinct materials, not by the data.

---

## Building

A footprint near the site. Produced by the backend; the browser does not re-derive height.

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | `osm_way_{id}`, mirroring `BoundaryCandidate`'s id convention. |
| `ring` | `{ latitude, longitude }[]` | Closed ring, ≥ 4 points. |
| `heightMetres` | `number` | Resolved by research D5's three-step rule (`height` tag → levels × 3 m → 9 m). |
| `heightProvenance` | `'known' \| 'assumed'` | FR-011. Computed backend-side where the raw tags are. |
| `name` | `string` | `name`/`name:en` tag, or empty. |
| `isSiteBuilding` | `boolean` | Research D8's stated rule. At most one is `true`. |

**Rendering note (research D13)**: a `Building` is extruded into shadow-casting geometry that is
**not drawn** — `colorWrite: false`, `depthWrite: false`, `castShadow: true`. The basemap already
draws its own 3D buildings; a second misaligned copy reads as a rendering bug. Nothing in this
entity changes as a result — the data is identical either way, which is why the developer toggle
that reveals the massing needs no separate model.

### Building Footprint Result (the endpoint's envelope)

| Field | Type | Notes |
|---|---|---|
| `buildings` | `Building[]` | Bounded by `maxCount`. |
| `limited` | `boolean` | `true` when the cap truncated the result (FR-015). |
| `excludedCount` | `number` | Footprints dropped as unusable (FR-013). |
| `radiusMetres` | `number` | Echoed so the UI can state the analysis area. |

---

## Analysis Moment

The date and time currently being shown.

| Field | Type | Notes |
|---|---|---|
| `instantUtc` | `Date` | The single source of truth. All display derives from it. |
| `localDate` | `string` | `YYYY-MM-DD` in the site's zone, for the date control. |
| `localMinuteOfDay` | `number` | 0…1439 in the site's zone, for the time slider. |
| `isPlaying` | `boolean` | Drives the guarded frame callback (research D9). |
| `playbackMinutesPerSecond` | `number` | Default 120 — a full day in ~12 s. |

**Rule**: `instantUtc` is canonical; `localDate`/`localMinuteOfDay` are projections through the
site's time zone. Editing either projection recomputes `instantUtc`, never the reverse — this is
what keeps FR-004 (DST transitions) correct, because a local time that occurs twice or not at all
resolves once, at one place.

---

## Correction

A user-supplied override for a site. Session-scoped (research D11).

| Field | Type | Notes |
|---|---|---|
| `siteKey` | `string` | Which site this applies to (FR-028). |
| `buildingHeights` | `Record<string, number>` | Building id → corrected metres. |
| `groundOffsetMetres` | `number` | Default 0. Applied as `group.position.z` on the extension's own Drawing Space — **never** by re-anchoring the scene (research D15, FR-041). |

**Validation (FR-027)**: height `> 0` and `≤ 1000`; ground offset within `±500`. A rejected value
keeps the previous one and states why — never a silent clamp, never a discarded edit.

---

## Analysis State (the store's own shape)

| Field | Type | Notes |
|---|---|---|
| `status` | `'idle' \| 'loading' \| 'ready' \| 'partial' \| 'failed'` | `partial` = sun path works but buildings did not (FR-014). |
| `failureReason` | `string \| null` | User-facing wording. Present only when `status === 'failed'`. |
| `buildingsNotice` | `string \| null` | "No buildings found here", "Building data unavailable", or the limited/excluded counts. |

### Lifecycle

```text
idle ──open──▶ loading ──buildings ok──────▶ ready
                  │
                  ├──no buildings / fetch failed──▶ partial   (sun path still works — FR-014)
                  │
                  └──no site / WebGL unsupported──▶ failed

ready|partial ──site changed──▶ loading        (research D10 — follow, don't close)
ready|partial|failed ──close──▶ idle           (FR-024/FR-040 — everything withdrawn)
```

**`partial` is not a failure state.** FR-014 requires the sun path keep working when building data
is missing, so the distinction between "we have less than we wanted" and "we have nothing" is
carried in the model rather than collapsed into an error.

**Every transition into `failed` or `partial` sets a user-facing string.** There is no path into
either state that leaves the user with nothing to read (FR-045/FR-046, SC-008, constitution §2.VIII).
