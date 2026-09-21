# Data Model: Solar Analysis Accuracy & Performance

**Feature**: specs/063-solar-accuracy-performance | **Date**: 2026-09-21

No persisted entity changes — this feature has no database, no API payload and no store schema
change. What follows models the *quantities* the feature computes and the relationships that must
hold between them, since the defect being repaired is precisely a relationship that did not hold.

---

## The altitude quantities

Three distinct quantities exist in the domain. The defect is that the current implementation uses
two of them without naming either.

| Quantity | Meaning | Status after this feature |
|---|---|---|
| **Geometric altitude** | Where the sun's centre actually is, ignoring the atmosphere | Internal only — a local intermediate inside `solarPosition()` and inside the rise/set solver. Never returned, never displayed, never used for rendering. |
| **Apparent altitude** | Where the sun's centre *appears* to be, after atmospheric refraction bends its light | **Authoritative.** The single altitude the system exposes. Drives the figure, the light direction, the shadows and the sun marker. |
| **Apparent upper-edge altitude** | Apparent altitude + the sun's angular radius | Used only to *define* rise and set. Not exposed as a figure. |

**Relationship**: `apparent = geometric + refraction(geometric)` and
`upperEdge = apparent + SOLAR_SEMIDIAMETER_DEGREES`.

**The invariant this feature establishes**: at the reported sunrise and sunset,
`upperEdge = 0`, and therefore `apparent = −SOLAR_SEMIDIAMETER_DEGREES = −0.267°` — at every
location, on every date. This constant is the feature's central testable claim (FR-002, SC-001).

---

## Named constants

Every value below is exported, so that assertions and user-facing wording share one source and
cannot drift — the property specs/052 established with its tolerance constants (T006).

| Constant | Value | Origin | Consumed by |
|---|---|---|---|
| `SOLAR_SEMIDIAMETER_DEGREES` | `0.2667` | The sun's mean angular radius (16′) | Rise/set definition; the expected figure in SC-001 |
| `SOLAR_POSITION_TOLERANCE_DEGREES` | `0.1`, **subject to re-measurement** | Measured against NOAA's published *corrected* column (research D10) | Figures panel wording; every position assertion |
| `RISE_SET_TOLERANCE_SECONDS` | `60` | Inherited from NOAA, unchanged | Rise/set assertions, \|lat\| ≤ 72° |
| `RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE` | `600` | Inherited from NOAA, unchanged | Rise/set assertions, \|lat\| > 72° |
| `HIGH_LATITUDE_THRESHOLD_DEGREES` | `72` | Existing | Selects which rise/set tolerance applies |
| `MIN_SHADOW_ELEVATION_DEGREES` | `1.0` | Research D7 — below this, shadow length diverges | Light enable/disable; the derived radius; user-facing copy |
| `SHADOW_GATE_DEGREES` | `0.25` | Research D8 — below the perceptible threshold | Playback recomputation gate |

`SHADOW_FRUSTUM_RATIO` (`1.3`) is **removed**. Research D6 established it has no derivation — it is
the prototype's hard-coded 260 m restated — and FR-008 replaces it with a computed value.

---

## The shadow radius

A single scalar, as today. Only its derivation changes.

**Today**: `analysisRadiusMetres × 1.3`, independent of what is in the scene.

**After**: derived from the footprints actually present —

```
buildingExtent   = max distance from the scene anchor to any footprint vertex
longestShadow    = tallestBuildingMetres / tan(MIN_SHADOW_ELEVATION_DEGREES)
shadowRadius     = buildingExtent + longestShadow
```

with a defined fallback to the present behaviour when no buildings are present (FR-013).

**The invariant that must survive** (specs/052 README constraint 4, research D16): this one value
sizes *both* the shadow camera's orthographic extent *and* `shadowGround.ts`'s ground plane, with
the ground plane strictly inside the frustum. The existing `shadowGround.test.ts` assertion — ground
half-extent strictly less than frustum half-extent for every tested radius — remains the mechanism
enforcing it, and must now pass for content-derived radii as well as for arbitrary ones.

---

## Sun-path drawing groups

`buildSunPath` currently returns one bag of objects rebuilt wholesale on every date change. It
becomes two groups with different lifetimes.

| Group | Contents | Depends on | Rebuilt when |
|---|---|---|---|
| **Fixed furniture** | Compass dial, mount post, monthly lattice (and `buildDomeShell`, which remains *built but not assembled* — README constraint 5) | Radius; latitude, longitude, year | Site changes, or year changes |
| **Dated path** | Day arc, hour marks, seasonal extremes, current-position marker | All of the above, plus the chosen date and instant | Date changes, or instant changes |

**Lifecycle rule**: each group owns its own disposal scope. The dated group is disposed and
rebuilt on date change; the fixed group is disposed only on site change, year change, or extension
stop. Both are disposed by `disposeAll`. This is the hazard research D9 identifies — splitting one
disposal scope into two is where leaks get introduced (FR-023, SC-010).

---

## Merged footprint geometry

| Aspect | Today | After |
|---|---|---|
| Geometry | One `ExtrudeGeometry` per building | One merged `BufferGeometry` for all buildings |
| Material | One `MeshStandardMaterial` per building | One shared material |
| Mass toggle | Traverse and set `colorWrite` on each | Set `colorWrite` on the one material |
| Rebuild trigger | New building data; a height correction | Unchanged |
| Per-building identity | Carried by the mesh, unused | Carried by `solarAnalysisStore.siteBuildings`, as it already is |

`castShadow: true`, `colorWrite: false`, `depthWrite: false` are unchanged in meaning — buildings
still cast shadows without being drawn over the basemap's own buildings (FR-017, README
constraint 1).

---

## State that does not change

For the avoidance of doubt, this feature touches none of the following: `solarAnalysisStore`'s
shape, `correctionsStore`'s session-scoped site-keyed overrides, the `/api/v1/site-buildings`
request or response, the panel content-block model from specs/049, or any framework-owned scene,
renderer or anchor state.
