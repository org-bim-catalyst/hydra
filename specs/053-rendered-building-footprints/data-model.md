# Phase 1 Data Model: Building Footprints from Rendered Map Imagery

**Nothing new is persisted and no existing shape changes.** This feature swaps where one existing
type is *produced* from — which is why specs/052 needs no modification (FR-019, SC-008).

---

## Existing types, reused unchanged

These come from specs/052 and are listed to make explicit that they are **not** being altered.

### `BuildingFootprint` (`Application/Buildings`)

| Field | Type | Change |
|---|---|---|
| `Id` | `string` | Now `rendered_{tileKey}_{index}` when the rendered source supplied it, `osm_way_{id}` when Overpass did. Still opaque to every consumer. |
| `Ring` | `IReadOnlyList<GeoPoint>` | Unchanged — closed ring, ≥ 4 points |
| `HeightMetres` | `double` | Unchanged |
| `HeightProvenance` | `known` \| `assumed` | Unchanged. Always `assumed` from the rendered source (research D8). |
| `Name` | `string` | Empty from the rendered source — imagery carries no names |
| `IsSiteBuilding` | `bool` | Unchanged; the containment rule applies the same way regardless of source |

### `BuildingFootprintResult`

| Field | Change |
|---|---|
| `Buildings`, `Limited`, `ExcludedCount`, `RadiusMetres` | Unchanged |
| `Source` | **Added** — `rendered` \| `osm` \| `none` (FR-015). Additive; existing consumers ignore it. |

`Source` is the one field this feature adds anywhere, and it exists purely so a coverage gap is
diagnosable instead of silent.

---

## Internal types (Infrastructure only, never cross a layer boundary)

### `RenderedFootprintOptions`

Bound from `Buildings:Rendered`, validated at startup.

| Setting | Default | Why this value (research) |
|---|---|---|
| `Zoom` | 18 | D3 — 0.27 m/px, one tile covers ~345 m > the 200 m radius |
| `MinimumAreaSquareMetres` | 15.0 | D5 — below any habitable structure, above threshold noise |
| `SimplifyTolerancePixels` | 3.0 | D3 — matches the existing vectorizer; contributes 0.81 m of the 1 m budget |
| `StatedPositionalToleranceMetres` | 1.0 | D3 — derived, not chosen; the figure SC-003 is measured against |
| `CacheTtl` | 15 minutes | D9 — matches `BuildingRetrievalOptions.CacheTtl`; this provider caches internally, the composite does not (see below) |

The style string is a **constant, not a setting**. It is verified behaviour (research D1), not an
environment-varying value, and §2.VII reserves configuration for things that genuinely vary.

### `BuildingMask`

The transient bridge between fetching and vectorising. Never leaves the provider.

| Field | Notes |
|---|---|
| `Mask` | `bool[,]` — true where the pixel is the forced building colour |
| `Width`, `Height` | 1280 × 1280 at `scale=2` |
| `Bounds` | `SatelliteImage` — the computed ground rectangle from `StaticMapFraming.CoveredBounds`, used for pixel→geo |
| `MetresPerPixel` | Used to convert `MinimumAreaSquareMetres` into a pixel count |

---

## Pipeline

```text
request (lat, lng, radius)
   │
   ├─ RenderedBuildingFootprintProvider
   │     fetch styled tile ──▶ threshold ──▶ BuildingMask
   │                                            │
   │     MaskContourVectorizer.ExtractAllRings(4-connected, min area)
   │                                            │
   │     ┌──────────────────────────────────────┴───────────────┐
   │     │ per component: trace ▸ simplify ▸ pixel→geo           │
   │     │ discard < min area (counted)                          │
   │     │ discard touching the tile edge (D6)                   │
   │     │ keep if any part inside radius (D6)                   │
   │     └──────────────────────────────────────┬───────────────┘
   │                                            ▼
   │                        footprints + excludedCount, Source = rendered
   │
   └─ CompositeBuildingFootprintProvider
         rendered returned ≥ 1 footprint ──▶ use it, Source = rendered
         rendered returned 0, or threw    ──▶ Overpass, Source = osm
         both empty or failed             ──▶ empty, Source = none
```

**Arbitration is whole-result, never per-building** (research D7): the two sources' polygons do not
coincide, so any merge renders every building twice, slightly offset, each casting its own shadow.

---

## Exclusion rules, and what each one protects

Every discard is counted into `ExcludedCount` so the user can be told (FR-009), rather than
silently dropped.

| Rule | Threshold | Protects against |
|---|---|---|
| Below minimum area | 15 m² | Threshold specks becoming shadow-casting slivers |
| Touches the tile edge | any edge pixel | A clipped footprint fabricating geometry beyond the frame |
| Fewer than 3 points after simplification | — | Degenerate rings that cannot be extruded |
| Entirely outside the radius | — | Work and shadow cost for buildings nobody asked about |

**A building partly outside the radius is kept whole** — the one case that is deliberately *not* an
exclusion (research D6), because truncating it would produce a wrong shadow in the output the user
actually looks at.

---

## Failure outcomes

`none` is a legitimate success, not an error: it means both sources agree there is nothing there.
specs/052 already renders that as its `partial` state with the sun path still working.

| Condition | `Source` | Surfaced as |
|---|---|---|
| Rendered source returned footprints | `rendered` | Normal result |
| Rendered empty/failed, Overpass returned | `osm` | Normal result — the fallback is not an error |
| Both empty | `none` | "No buildings found near this site" |
| Both failed | `none` | "Building data is unavailable" + `503` per the existing contract |

No path leaves the caller without a typed outcome, and none is observable only in logs
(FR-021/FR-022, constitution §2.VIII).
