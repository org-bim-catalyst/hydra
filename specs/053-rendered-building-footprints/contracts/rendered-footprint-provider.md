# Contract: Rendered Building Footprint Provider

`RenderedBuildingFootprintProvider` — an `Infrastructure` implementation of the
`IBuildingFootprintProvider` interface `Application` already owns (specs/052).

```csharp
Task<BuildingFootprintResult> SearchAsync(
    GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default);
```

**The interface is not modified.** That it already fits is the strongest evidence specs/052 drew
the seam in the right place, and it is what makes FR-019/SC-008 true by construction rather than by
testing.

## The request

Exactly one Static Maps tile, fetched through the existing `GoogleStaticMaps` named `HttpClient`:

| Parameter | Value | Source |
|---|---|---|
| `center` | the requested point | caller |
| `zoom` | **18** | research D3 |
| `size` | `640x640`, `scale=2` | matches the boundary path; yields 1280² |
| `maptype` | **roadmap** | research D1 — terrain renders no buildings at all |
| `format` | `png` | lossless; JPEG artefacts blur the threshold edge |

Three style rules, in this order:

```
feature:all|element:labels|visibility:off
feature:all|element:geometry|color:0xffffff
feature:landscape.man_made.building|element:geometry|color:0xff00ff
```

### Guarantees about the request

- **The style string is a constant.** No caller input reaches it. The only interpolated values are
  already-validated coordinates and a fixed zoom (§8).
- **The image is never shown to anyone.** Fetched in memory, held for the duration of the
  conversion, discarded (FR-002). It is not written to disk, not cached as an image, and not
  returned through any endpoint.
- **One tile, no stitching.** At zoom 18 a tile covers ~345 m, comfortably beyond the 200 m default
  radius (research D3) — unlike the boundary path, which stitches four tiles for large sites.

## The conversion

```text
bytes ─▶ Image.Load<Rgba32> ─▶ threshold ─▶ bool[,] mask
      ─▶ MaskContourVectorizer.ExtractAllRings(...)
      ─▶ filter ─▶ BuildingFootprintResult
```

**Threshold**: a pixel is a building pixel when `R ≥ 180 && B ≥ 180 && G ≤ 120` — the magenta
forced by the style. This mirrors the boundary extractor's own green test in shape and tolerance;
the wide band absorbs edge antialiasing without admitting white.

**Vectorisation** reuses `MaskContourVectorizer` in full — component labelling, pixel-grid edge
tracing, Douglas-Peucker, and pixel→geo through `StaticMapFraming.CoveredBounds`. Two parameters
differ from the boundary path and both are deliberate:

| Parameter | Boundary path | Here | Why (research) |
|---|---|---|---|
| Connectivity | 8-connected | **4-connected** | D4 — 8 merges diagonally-touching buildings into one giant footprint |
| Minimum size | 0.15 × image diagonal | **15 m² ground area** | D5 — the boundary threshold would discard every real building |

### Guarantees about the result

- **Every returned ring is closed and has ≥ 3 distinct points** after simplification. Anything less
  is excluded and counted.
- **Rings are in real-world coordinates**, accurate to a stated **±1 m** — derived from 0.27 m/px
  plus the 3-pixel simplification tolerance (research D3), not chosen.
- **`HeightProvenance` is always `assumed`** and `HeightMetres` is the stated default. Imagery
  carries no height, and matching footprints to OSM tags to borrow one is a cross-source alignment
  problem this feature deliberately does not attempt (research D8).
- **`Name` is empty.** Imagery carries no names either.
- **`Source` is `rendered`** (FR-015).
- **`ExcludedCount` counts every discard** — too small, tile-edge-clipped, degenerate — so the user
  can be told rather than silently shown fewer buildings than exist (FR-009).
- **`Limited` honours the existing count cap** unchanged.

### Stated limitation

Buildings that **share a wall** in the rendering merge into one footprint. 4-connectivity separates
diagonal contact but cannot separate a shared edge, and no morphological erosion is attempted —
the boundary path's own history records that a square structuring element chamfers every
non-grid-aligned corner, which would damage far more footprints than it separates. FR-010 permits a
stated limitation, and this is it.

## Caching

*Added after `/speckit-analyze` found this contract silent on it while plan.md wrongly claimed it
was inherited from elsewhere.* This provider caches its own result **internally**, in
`SearchAsync`, mirroring `OverpassBuildingFootprintProvider`'s existing pattern exactly: check the
cache, fetch and vectorise on a miss, store with `RenderedFootprintOptions.CacheTtl` (default 15
minutes, matching Overpass's). Keyed the same way — rounded coordinates plus radius. The composite
provider (contracts/footprint-source-arbitration.md) wraps neither inner provider; caching only
exists here and in the fallback, independently (research D9).

## Failure

| Condition | Behaviour |
|---|---|
| Tile fetch fails or returns a non-image | Throw `BuildingProviderUnavailableException` — the composite treats it as "try the fallback" |
| Tile is valid but contains no building pixels | Return an **empty result, not an exception** — a genuinely empty area and a coverage gap are indistinguishable here, and neither is an error |
| Every component is excluded | Empty result with `ExcludedCount > 0`, so "we found things and rejected them" stays distinguishable from "there was nothing" |

The empty-versus-exception distinction is what drives arbitration: an exception means *this source
is broken*, an empty result means *this source has nothing*. Both fall back, but only one is worth
recording as a fault.
