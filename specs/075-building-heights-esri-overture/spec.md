# SPEC-075: Measured Building Heights (Esri) and Overture Footprints

**Status:** Implemented 2026-09-26
**Depends on:** specs/052-solar-analysis, specs/053-rendered-building-footprints

## Problem

Solar and shadow analysis draws every building at an assumed 9 m unless OpenStreetMap has a
height tag. In Dubai almost none do, so shadows are wrong wherever a building is not 9 m tall,
and they look detached from the building that casts them.

## Decision

1. **Heights come from Esri's global 3D Buildings scene layer (I3S).** It carries measured
   heights from Vantor for Dubai. A new `IBuildingHeightSource` (Application) is implemented by
   `EsriBuildingHeightSource` (Infrastructure).
2. **Overture Maps is added to the footprint chain**, between rendered imagery and OSM:
   rendered → Overture → OSM. The first non-empty source wins, and results are never merged.
3. **`HeightEnrichingBuildingFootprintProvider`** decorates the chain. It fetches heights in
   parallel and replaces an *assumed* height with the tallest measured height inside the outline.
   A *known* height is never overwritten.
4. **Licensing.** The platform is non-commercial today (an MCP, not a paid product), so using
   Esri's layer under its terms is accepted. Revisit this before any commercial launch.
   Overture is ODbL / CDLA-Permissive.

## Functional requirements

- **FR-001** An assumed-height footprint with one or more measured heights inside its outline
  takes the tallest of them and becomes `known`.
- **FR-002** A footprint whose height is already `known` keeps it.
- **FR-003** If the height source fails (network, parse, timeout), the footprints are returned
  unchanged and the failure is logged. Caller cancellation still propagates.
- **FR-004** Heights are searched 100 m beyond the requested radius. This ensures that
  footprints kept whole across the edge still match their heights.
- **FR-005** Overture is tried after rendered imagery and before OSM. An unavailable source
  falls through to the next one; only the last source's failure surfaces as a 503.
- **FR-006** Overture heights, in priority order:
  1. `height` → known.
  2. Tallest `building_part` height → known.
  3. `num_floors × 3` → assumed.
  4. Otherwise 9 m → assumed.

  Underground buildings are skipped.
- **FR-007** Only Vantor-sourced Esri heights are trusted as measured. For any other source,
  a height of exactly 3.0 m is the layer's "no height" placeholder and is ignored.
- **FR-008** The response's `source` reports `overture` when Overture supplied the footprints.
  The frontend type mirrors this as an optional field.
- **FR-009** Both integrations can be switched off with `Buildings:Esri:Enabled` and
  `Buildings:Overture:Enabled`. Every option has a code default, so no appsettings change is
  required.

## Research findings

### Overture

- The archive is at
  `https://overturemaps-extras-us-west-2.s3.us-west-2.amazonaws.com/tiles/{release}/buildings.pmtiles`
  (PMTiles v3, gzip directories and tiles, MVT, max zoom 14). Releases are listed with S3
  `ListObjectsV2` (`list-type=2&prefix=tiles/&delimiter=/`).
- A just-listed release can still be missing its archive, so the newest three are tried.
- **Overture has no heights in residential Dubai.** An external claim that it does was only
  partly right. Its heights are OSM-derived and exist where OSM mappers added them: Downtown
  has them (Burj Khalifa at 828 m), but Al Safa and BurJuman have essentially none.
- MVT tiles carry a buffer beyond the tile edge, so each ring is clipped (Sutherland–Hodgman) to
  the tile's exact bounds. A building spanning tiles is therefore split into pieces, not
  duplicated.

### Esri I3S

- The layer is a node-page tree. The root box spans the whole Earth. Node boxes are oriented
  bounding boxes in Earth-centred coordinates, and the centre is given in lon/lat/height. Pruning
  tests the site's distance to each box with 100 m of slack.
- Leaf geometry is Draco-compressed (decoded with `Openize.Drako` 26.2.0). Positions are offsets
  from the node centre, multiplied by `i3s-scale_x` / `i3s-scale_y` from the Draco attribute
  metadata. The library does not expose that metadata, so the header is parsed by hand.
- Attributes are the `height` field (Float32) and the `source` field (String). One building is one
  feature, and its location is the centre of its vertex bounding box.
- Heights from non-Vantor sources (OSM) are 3.0 m when unknown, which is why FR-007 exists.

### Live probe (2026-09-26, dev, before the probe was removed)

| Site | Radius | Overture buildings | Known after enrichment |
|---|---|---|---|
| Al Safa Park 2 | 200 m | 129 | 48 |
| BurJuman | 300 m | 100 | 64 |
| Downtown | 400 m | — | Burj Khalifa 828 m (from Overture) |

- **Timing:** Overture takes about 2–3 s cold. Esri takes 0.4–0.7 s cold and about 0.2 s once its
  node pages are cached. The two run concurrently.

## Known caveats

- Vantor heights for the Al Safa villa area look low (median about 5.7 m). They have not yet been
  checked against ground truth.
- The UI shows no attribution line yet. The attributions are "© OpenStreetMap contributors,
  Overture Maps Foundation" and "Esri, Vantor".
- The rendered-imagery source still wins whenever it returns anything, so Overture is used only
  when rendered imagery is empty or unavailable. Enrichment applies to both.

## Tests

`tests/AskLucy.Infrastructure.Tests/Buildings/` covers:

- `Overture/` — PMTiles/MVT decoding, clipping, release fallback, and the height order.
- `Esri/` — tree pruning, the placeholder rule, gzip, the key header, caching, the typed
  unavailable exception, and the OBB/attribute/Draco primitives.
- `HeightEnrichingBuildingFootprintProviderTests`.
- `CompositeBuildingFootprintProviderTests` — chain behaviour.

`GeometryMathTests` covers `GeometryMath.Contains`.
