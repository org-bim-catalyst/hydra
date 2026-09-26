# Buildings

Building footprints for solar analysis (specs/052), retrieved from an ordered fallback chain: the
map provider's own rendered imagery first (specs/053), Overture Maps second, OpenStreetMap last
(specs/075). Measured heights from Esri's 3D buildings layer are then applied to footprints whose
height was only assumed (specs/075).

## Why two sources

OpenStreetMap coverage is unreliable — at a test site in Cairo it reports zero buildings within
200 m while the map renders dozens there — and Overpass is the least reliable dependency this
platform has, routinely returning 429/504 under load. Rendered imagery fixes the coverage problem;
Overpass stays as a fallback because the two fail for **unrelated reasons** (styling/coverage vs.
load), which is what makes the pair worth more than either alone.

## The verified style — do not retry the obvious first attempt

```
maptype=roadmap
style=feature:all|element:labels|visibility:off
style=feature:all|element:geometry|color:0xffffff
style=feature:landscape.man_made.building|element:geometry|color:0xff00ff
```

This produces a **binary image**: solid magenta footprints on pure white, nothing else. Two things
that look reasonable and are not:

- **Plain `landscape.man_made` (no `.building`) floods the entire urban fabric.** Every city block
  renders magenta; buildings are only faintly outlined inside it. Completely unusable for
  thresholding. This is the obvious first attempt and it is wrong — verified live before building
  anything on it.
- **`maptype=terrain` renders no buildings at all, at any size.** specs/042 chose terrain
  specifically *because* of this (it wanted buildings absent, for site-boundary tracing). This
  feature needs the opposite and uses `roadmap`.

## Vectorisation

Reuses `MaskContourVectorizer` (`../Boundaries/`) — the same flood-fill/edge-trace/Douglas-Peucker/
pixel-to-geo pipeline the site-boundary path already proved — via an additive entry point,
`ExtractAllRings`, that returns every usable component instead of only the largest. Two things
differ from the boundary path's own defaults, both deliberate:

| | Boundary path | Buildings | Why |
|---|---|---|---|
| Connectivity | 8-connected | **4-connected** | 8-connectivity merges anything touching even diagonally — correct for one boundary, wrong for buildings: it would merge diagonally-adjacent footprints into one giant shadow-caster. |
| Minimum size | 0.15 × image diagonal | **15 m² ground area** | The boundary threshold assumes a boundary fills most of the frame; at that threshold nearly every real building would be discarded. |

**Stated limitation**: buildings that share a full wall (not just a corner) in the rendering merge
regardless of connectivity — they are genuinely one connected blob in the source pixels. No
morphological erosion is attempted to split them: the boundary path's own history records that a
square structuring element chamfers every non-grid-aligned corner, which would damage far more
footprints than it separates.

**A real bug this feature's own tests caught**: `MaskContourVectorizer.AllComponents` filters
out below-minimum-area components internally, before `ExtractAllRings`'s loop ever sees them.
Counting exclusions only from the *filtered* list silently undercounted `ExcludedCount` — a
too-small blob vanished instead of being reported (FR-009). Fixed by comparing against an
unfiltered component count inside `ExtractAllRings` itself; see `SearchAsync_ShouldApplyEveryExclusionRule_AndCountEachDiscard_WithoutFailingTheWholeResult`
in the test suite, which pins this with a synthetic tile built to exercise all four exclusion rules
in one image.

## Positional tolerance

Stated as **±1 m**, derived from the tile's own resolution at zoom 18/scale 2 (~0.27 m/px in
Dubai/Cairo) plus the 3-pixel Douglas-Peucker simplification tolerance — inherited from the
mathematics, not chosen. One tile (~345 m coverage) comfortably exceeds the 200 m default analysis
radius, so no stitching is needed, unlike the boundary path's own four-tile stitch for larger sites.

## Heights are unchanged, and deliberately not cross-matched

> **Superseded by specs/075 and specs/076** — see "Arbitration and conflation" and "Measured
> heights" below. specs/076 does now pass a height between outlines, but only when one covers at
> least half of the other on a 1 m grid; the loose join rejected here is still rejected.

A rendered image carries no height data. Every rendered footprint gets the stated default height
(9.0 m, matching `OverpassBuildingFootprintProvider`'s own default exactly) marked `assumed` — never
`known`. Matching a rendered footprint to an OSM height tag was considered and rejected: it is a
spatial join between two independently-derived geometries, and getting it wrong attaches one
building's height to another's outline, which is worse than an honest default because the user is
told it was recorded. The trade accepted here is more assumed heights in exchange for footprints
existing at all where OSM has none — and the user's existing height-correction control
(specs/052) is the escape hatch for it.

## Caching

Each provider caches its own result **internally** — there is no shared wrapper. This was corrected
during implementation after `/speckit-analyze` found the original design assumed a cache wrapper
that never existed (`OverpassBuildingFootprintProvider` has always cached inside its own
`SearchAsync`). `RenderedBuildingFootprintProvider` now does the identical thing under its own
`Buildings:Rendered` config section, with a deliberately different cache-key prefix so the two
providers sharing one `IMemoryCache` instance can never collide.

## Arbitration and conflation (specs/076)

`CompositeBuildingFootprintProvider` asks all three sources — rendered, Overture, OSM — **at the
same time** and conflates their answers (`BuildingFootprintConflation`). Superseding specs/075's
"first non-empty source wins, never merged":

- The **primary** is the highest-priority source that found anything; all of its footprints are
  kept. Lower sources get `Buildings:Conflation:StragglerBudget` (default 8 s) after the primary
  answers, then are cancelled and logged as too slow.
- A lower source's footprint is **gap-filled** (drawn) only if less than 30 % of it overlaps what is
  already kept, on a 1 m grid. Anything more is a *counterpart* — the same building traced by
  another source, a few metres off — and is never drawn, which is what keeps specs/052's "ghost
  duplicate" from coming back.
- A counterpart covering at least half of a kept *assumed* footprint passes on its height: a
  Known height makes it Known; a storey-derived height replaces the bare 9 m default but stays
  Assumed. A Known height is never replaced.
- The site building is the largest flagged candidate from any source; if that is a counterpart,
  the role moves to the kept footprint it overlaps most. The result stops at
  `Buildings:Overpass:MaxBuildingCount` (default 1000) and says so (`Limited`). The client widens
  the radius to cover a large site's boundary, up to 500 m (about 400 buildings in a dense
  district), so the old cap of 300 was too low.
- Failures are logged per source. Only if **every** source fails is the last error rethrown (the
  endpoint's 503). *Behaviour change:* if one source answered empty and the rest failed, the
  result is empty (`Source = None`), not a 503 — "no buildings here" was an answer.

Depends on `IBuildingFootprintProvider` via keyed DI (`"rendered"`/`"overture"`/`"osm"`), not on
any concrete provider type — they are `sealed`, and a concrete-type constructor could never
actually be unit-tested, since NSubstitute cannot proxy a sealed class.

The unkeyed `IBuildingFootprintProvider` is `HeightEnrichingBuildingFootprintProvider`, a decorator
over the composite (keyed `"footprints"`).

## Overture Maps (specs/075)

`Overture/OvertureBuildingFootprintProvider` reads Overture's buildings theme from the PMTiles
archive the Overture Maps Foundation publishes on S3
(`overturemaps-extras-us-west-2`, `tiles/{release}/buildings.pmtiles`) with HTTP range requests —
no API key, no SDK. Releases are discovered from the bucket listing, newest first, trying up to
three (a just-listed release can still be uploading). Directories are cached for 24 h; tiles are
read at zoom 14, the archive's maximum.

- Only exterior rings are used (positive signed area); holes are dropped, as everywhere else here.
- A building crossing a tile edge is clipped to each tile's exact bounds (the tiles carry a buffer)
  and kept as pieces `overture_{id}_{n}`; shadows are unaffected, and every piece of the site's own
  building is flagged as the site building.
- Height order: `height` → Known; tallest `building_part` height → Known; `num_floors × 3` →
  Assumed; otherwise 9 m → Assumed. Underground buildings are skipped.
- **Overture has no heights in residential Dubai.** Its heights are OSM-derived and exist only
  where OSM mappers added them (Downtown — Burj Khalifa comes through at 828 m). Across Al Safa
  and BurJuman almost every footprint is assumed until Esri enriches it.
- Licence: ODbL (OSM-derived) / CDLA-Permissive (Microsoft footprints) — attribution
  "© OpenStreetMap contributors, Overture Maps Foundation".

## Measured heights (Esri, specs/075, reworked by specs/076)

`HeightEnrichingBuildingFootprintProvider` runs the footprint composite and
`Esri/EsriBuildingHeightSource` concurrently, searching heights 100 m beyond the radius (the
footprints of buildings that straddle the edge are kept whole). The height source returns a
**`BuildingHeightMap`** — a 2 m raster of roof heights, `NaN` where nothing was measured — not one
height per building. specs/075's one-point-per-building match failed at BurJuman: Esri models the
mall and its tower as **one feature** whose only height is the tower top, so the whole mall was
drawn 105 m tall.

- An **assumed** footprint with at least half its cells measured takes their **median** and
  becomes Known. A Known height is never overwritten. A cell under several footprints belongs to
  the smallest.
- A **taller part** inside a footprint (more than max(3 m, 15 %) above its base, at least 20 m²)
  becomes an extra Known caster `{id}_part{n}`, recursing up to three setbacks. The frontend
  treats `{siteId}_part…` as part of the building under study.
- **Gap-fill:** measured roofs no footprint claims (claimed cells dilated by 2 cells, candidates
  opened with a 3×3 element) of at least 30 m² with their centroid inside the radius become
  `esri_{n}`; one becomes the site building only if no source found one.
- A height-source failure is logged and the footprints come back unchanged — heights are an
  enhancement, never a reason to fail the search.

`EsriBuildingHeightSource` reads Esri's global 3D Buildings I3S scene layer directly:

- Walks node pages breadth-first, pruning any node whose oriented bounding box (Earth-centred,
  quaternion-rotated) is more than 100 m beyond the search radius. Layer description cached 1 h,
  node pages 24 h (keyed by layer version).
- Leaf geometry is Draco-compressed; it is decoded with `Openize.Drako` and each measured
  feature's roof triangles are rasterised (`RoofHeightRasterizer`). A feature's ground is its top
  vertex minus its height attribute — its walls run below ground, so neither the lowest vertex nor
  zero is the ground. Draco positions are offsets from the node centre, scaled by
  the `i3s-scale_x/y` attribute metadata, which is read from the Draco header by hand because the
  library does not expose it.
- Attributes `height` (Float32) and `source` (String). **Vantor** heights are measured and always
  trusted. Every other source uses exactly 3.0 m as a "no height" placeholder, which is dropped.
- The optional `Esri:ApiKey` is sent as `X-Esri-Authorization: Bearer …` only when configured.
  ArcGIS reports a rejected or expired key (498/499) as **HTTP 200 with a JSON `error` body**; the
  layer is public, so the source drops the key, retries anonymously and remembers the rejection
  for the layer-cache TTL.
- Licence: Esri terms, accepted for non-commercial use — attribution "Esri, Vantor". Revisit before
  the platform goes commercial.

## Evaluated and rejected as alternative sources (spec.md Downstream)

Three published, ready-made sources were checked against this platform's actual sites (Dubai,
Cairo) before rendered imagery was chosen: open building footprints, open building heights, and the
provider's own solar analysis service. **All three exclude the Gulf.** This is not a one-off gap —
each is derived from an aerial/satellite processing programme that has not reached the Arabian
Peninsula, and any future source of that kind should be assumed to have the same hole until
checked. Rendered imagery works precisely because it reads the basemap the provider already draws
everywhere, not a derived dataset with a coverage frontier.

**Update (specs/075):** that finding held for the sources checked then, but Esri's 3D Buildings
layer does carry measured (Vantor) heights for Dubai, and Overture's footprints cover it — both are
now in the chain above.
